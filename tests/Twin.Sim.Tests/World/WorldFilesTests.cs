using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Sim.Tests.World;

/// <summary>
/// Reading a preprocessed world (manifest.json + raw little-endian layers, row 0 = south) (W1). The manifest's crs must be
/// EPSG:3067, every file it names must be inside the world directory, and a layer that is an unfetched Git LFS pointer
/// says so (pvarki/markdown-projectmatrix-pm#86).
/// </summary>
public class WorldFilesTests
{
    // Asymmetric on purpose: a flipped, mirrored or transposed read gives a different answer.
    private const int Columns = 3;
    private const int Rows = 2;

    private static readonly float[] Heights = [1, 2, 3, 4, 5, 6];

    private static readonly TerrainClass[] Classes =
    [
        TerrainClass.Road, TerrainClass.Open, TerrainClass.Open,       // row 0 (south)
        TerrainClass.Forest, TerrainClass.Forest, TerrainClass.Water,  // row 1 (north)
    ];

    [Fact]
    public void W1_row_0_of_a_layer_file_is_the_southernmost_row()
    {
        using var files = TestWorldFiles.Write(Columns, Rows, Heights, Classes);

        var world = WorldFiles.Load(files.Directory).World;

        Assert.Equal(TerrainClass.Road, world.ClassAt(1, 1));     // south-west cell
        Assert.Equal(TerrainClass.Water, world.ClassAt(5, 3));    // north-east cell
        Assert.Equal(1, world.GroundHeight(1, 1), 6);
        Assert.Equal(6, world.GroundHeight(5, 3), 6);
    }

    [Fact]
    public void Loaded_world_has_the_size_and_origin_from_the_manifest()
    {
        using var files = TestWorldFiles.Write(Columns, Rows, Heights, Classes, originEasting: 385000, originNorthing: 6672000);

        var loaded = WorldFiles.Load(files.Directory);

        Assert.Equal(new WorldBounds(0, 0, Columns * 2.0, Rows * 2.0), loaded.World.Bounds);
        Assert.Equal(2.0, loaded.World.CellSize);
        Assert.Equal(new GeoOrigin(385000, 6672000), loaded.Origin);
    }

    [Fact]
    public void Layer_whose_checksum_does_not_match_the_manifest_is_rejected()
    {
        using var files = TestWorldFiles.Write(Columns, Rows, Heights, Classes);
        var layer = files.PathOf("maastoluokka.bin");
        var bytes = File.ReadAllBytes(layer);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(layer, bytes);

        var error = Assert.Throws<InvalidDataException>(() => WorldFiles.Load(files.Directory));
        Assert.Contains("maastoluokka", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EPSG:4326")]
    [InlineData(null)]
    public void A_manifest_with_another_crs_or_none_is_rejected(string? crs)
    {
        using var files = TestWorldFiles.Write(Columns, Rows, Heights, Classes);
        EditManifest(files.Directory, manifest =>
        {
            manifest.Remove("crs");
            if (crs is not null)
            {
                manifest["crs"] = crs;
            }
        });

        var error = Assert.Throws<InvalidDataException>(() => WorldFiles.Load(files.Directory));
        Assert.Contains("crs", error.Message, StringComparison.Ordinal);
        Assert.Contains("EPSG:3067", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("h_maa", "../h_maa.bin")]
    [InlineData("h_maa", "<absolute>")]
    [InlineData("h_maa", "sub/../../h_maa.bin")]
    [InlineData("cliffs", "../jyrkanteet.json")]
    public void A_manifest_file_outside_the_world_directory_is_rejected(string entry, string file)
    {
        // <root>/world holds the world; a valid copy of the file the manifest points to lies at <root>/, so without the
        // check the world would load.
        using var files = TestWorldFiles.Write(Columns, Rows, Heights, Classes);
        var root = Path.Combine(Path.GetTempPath(), "twin-outside-" + Path.GetRandomFileName());
        var world = Path.Combine(root, "world");
        try
        {
            Directory.CreateDirectory(Path.Combine(world, "sub"));
            foreach (var path in Directory.GetFiles(files.Directory))
            {
                File.Copy(path, Path.Combine(world, Path.GetFileName(path)));
            }

            var target = file == "<absolute>" ? Path.Combine(root, "h_maa.bin") : file;
            if (entry == "cliffs")
            {
                var cliffs = Encoding.UTF8.GetBytes("""{ "lines": [] }""");
                File.WriteAllBytes(Path.Combine(root, "jyrkanteet.json"), cliffs);
                EditManifest(world, manifest => manifest["cliffs"] = new JsonObject
                {
                    ["file"] = target,
                    ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(cliffs)),
                });
            }
            else
            {
                File.Copy(files.PathOf("h_maa.bin"), Path.Combine(root, "h_maa.bin"));
                EditManifest(world, manifest => manifest["layers"]![0]!["file"] = target);
            }

            var error = Assert.Throws<InvalidDataException>(() => WorldFiles.Load(world));
            Assert.Contains(target, error.Message, StringComparison.Ordinal);
            Assert.Contains("inside the world directory", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("h_maa")]        // 4 bytes per cell: the pointer has the wrong size
    [InlineData("maastoluokka")] // 1 byte per cell, one cell per pointer byte: the right size, the wrong checksum
    public void A_layer_that_is_a_Git_LFS_pointer_says_so(string layer)
    {
        const string pointer = "version https://git-lfs.github.com/spec/v1\noid sha256:" +
                               "0000000000000000000000000000000000000000000000000000000000000000\nsize 250000\n";
        var cells = Encoding.UTF8.GetByteCount(pointer);
        using var files = TestWorldFiles.Write(cells, 1, new float[cells], Enumerable.Repeat(TerrainClass.Open, cells).ToArray());
        File.WriteAllText(files.PathOf(layer + ".bin"), pointer);

        var error = Assert.Throws<InvalidDataException>(() => WorldFiles.Load(files.Directory));
        Assert.Contains(layer, error.Message, StringComparison.Ordinal);
        Assert.Contains("Git LFS pointer", error.Message, StringComparison.Ordinal);
    }

    private static void EditManifest(string directory, Action<JsonObject> edit)
    {
        var path = Path.Combine(directory, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(manifest);
        File.WriteAllText(path, manifest.ToJsonString());
    }
}
