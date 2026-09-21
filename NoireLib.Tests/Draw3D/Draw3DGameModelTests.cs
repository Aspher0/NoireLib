using FluentAssertions;
using Lumina;
using Lumina.Data.Parsing.Layer;
using NoireLib.Draw3D.Assets;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the game model parser against real game files: the layout walk must land exactly on the
/// declared runtime block size, and the decoded geometry must be finite and plausibly sized.<br/>
/// These tests need a local game installation and skip cleanly without one. They are a
/// correctness gate on machines that have the game, inert everywhere else.
/// </summary>
public class Draw3DGameModelTests
{
    /// <summary>Both position encodings, both material path styles, a range of sizes.</summary>
    private static readonly string[] SampleModels =
    [
        "bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl",
        "bgcommon/hou/indoor/general/0002/bgparts/fun_b0_m0002.mdl",
        "chara/equipment/e0001/model/c0101e0001_top.mdl",
        "chara/human/c0101/obj/body/b0001/model/c0101b0001_top.mdl",
        "chara/monster/m0001/obj/body/b0001/model/m0001b0001.mdl",
    ];

    [Fact]
    public void LoadFile_RealGameModels_WalksLayoutExactly()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var parsed = 0;
        foreach (var path in SampleModels)
        {
            if (!game.FileExists(path))
                continue;

            // A mis-sized block anywhere in the runtime walk makes LoadFile throw.
            var file = game.GetFile<GameModelFile>(path);

            file.Should().NotBeNull();
            file!.Meshes.Should().NotBeEmpty(because: $"'{path}' must expose at least one mesh");
            file.Lods.Should().HaveCount(3);
            file.Declarations.Should().NotBeEmpty();
            parsed++;
        }

        parsed.Should().BeGreaterThan(0, because: "the sample paths should exist in any complete installation");
    }

    [Fact]
    public void Decode_RealGameModels_ProducesFiniteBoundedGeometry()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var decoded = 0;
        foreach (var path in SampleModels)
        {
            if (!game.FileExists(path))
                continue;

            var meshes = GameModelLoader.Decode(game.GetFile<GameModelFile>(path)!);
            meshes.Should().NotBeEmpty(because: $"'{path}' has geometry in its first level of detail");

            foreach (var mesh in meshes)
            {
                mesh.Geometry.Vertices.Should().NotBeEmpty();
                mesh.Geometry.Indices.Should().NotBeEmpty();

                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                foreach (var vertex in mesh.Geometry.Vertices)
                {
                    float.IsFinite(vertex.Position.X).Should().BeTrue();
                    float.IsFinite(vertex.Position.Y).Should().BeTrue();
                    float.IsFinite(vertex.Position.Z).Should().BeTrue();
                    min = Vector3.Min(min, vertex.Position);
                    max = Vector3.Max(max, vertex.Position);
                }

                var extent = (max - min).Length();
                extent.Should().BeGreaterThan(0.001f, because: "a decoded mesh with zero extent means the stride or offset is wrong");
                extent.Should().BeLessThan(10000f, because: "an enormous extent means the position element was read with the wrong format");

                foreach (var index in mesh.Geometry.Indices)
                    index.Should().BeLessThan((ushort)mesh.Geometry.Vertices.Length, because: "indices address their own mesh's vertices");
            }

            decoded++;
        }

        decoded.Should().BeGreaterThan(0);
    }

    /// <summary>The model's own bounding box, reached by the layout walk past the per-bone boxes. It backs box-shaped collision placements in the game.</summary>
    [Fact]
    public void LoadFile_RealGameModels_ReadsABoundingBoxThatEnclosesTheGeometry()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var checkedModels = 0;
        foreach (var path in SampleModels)
        {
            if (!game.FileExists(path))
                continue;

            var model = game.GetFile<GameModelFile>(path)!;

            model.BoundingBoxMax.X.Should().BeGreaterThanOrEqualTo(model.BoundingBoxMin.X);
            model.BoundingBoxMax.Y.Should().BeGreaterThanOrEqualTo(model.BoundingBoxMin.Y);
            model.BoundingBoxMax.Z.Should().BeGreaterThanOrEqualTo(model.BoundingBoxMin.Z);
            (model.BoundingBoxMax - model.BoundingBoxMin).Length().Should().BeGreaterThan(0.001f,
                because: "a zero-extent box means the walk stopped short of the bounding boxes");

            // The box bounds every LOD and shape. The decoded geometry is the first LOD's rest pose.
            var slack = 0.05f + (model.BoundingBoxMax - model.BoundingBoxMin).Length() * 0.05f;
            foreach (var mesh in GameModelLoader.Decode(model))
            {
                foreach (var vertex in mesh.Geometry.Vertices)
                {
                    vertex.Position.X.Should().BeInRange(model.BoundingBoxMin.X - slack, model.BoundingBoxMax.X + slack);
                    vertex.Position.Y.Should().BeInRange(model.BoundingBoxMin.Y - slack, model.BoundingBoxMax.Y + slack);
                    vertex.Position.Z.Should().BeInRange(model.BoundingBoxMin.Z - slack, model.BoundingBoxMax.Z + slack);
                }
            }

            checkedModels++;
        }

        checkedModels.Should().BeGreaterThan(0);
    }

    /// <summary>Both shader packages and both color table layouts.</summary>
    private static readonly string[] SampleMaterials =
    [
        "bgcommon/hou/indoor/general/0001/material/fun_b0_m0001_1a.mtrl",
        "bgcommon/hou/indoor/general/0002/material/fun_b0_m0002_1a.mtrl",
        "chara/equipment/e0001/material/v0001/mt_c0101e0001_top_a.mtrl",
        "chara/monster/m0001/obj/body/b0001/material/v0001/mt_m0001b0001_a.mtrl",
    ];

    [Fact]
    public void LoadFile_RealGameMaterials_WalksLayoutExactly()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var parsed = 0;
        foreach (var path in SampleMaterials)
        {
            if (!game.FileExists(path))
                continue;

            // A walk not ending on the declared file size throws.
            var file = game.GetFile<GameMaterialFile>(path);

            file.Should().NotBeNull();
            file!.ShaderPackage.Should().EndWith(".shpk", because: "every material names the package it draws with");
            file.Textures.Should().NotBeEmpty();
            file.Samplers.Should().NotBeEmpty();
            parsed++;
        }

        parsed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GameShaderNames_ResolvesEverySamplerInRealMaterials()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var checkedSamplers = 0;
        foreach (var path in SampleMaterials)
        {
            if (!game.FileExists(path))
                continue;

            var file = game.GetFile<GameMaterialFile>(path)!;
            foreach (var sampler in file.Samplers)
            {
                // Sampler ids are a checksum of the name. A miss means the derivation or the list drifted.
                GameShaderNames.NameOf(sampler.SamplerId).Should().NotBeNull(
                    because: $"sampler 0x{sampler.SamplerId:X8} in '{path}' should resolve from the shipped name list");
                checkedSamplers++;
            }
        }

        checkedSamplers.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BaseColorPath_RealMaterials_FindsATexture()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var resolved = 0;
        foreach (var path in SampleMaterials)
        {
            if (!game.FileExists(path))
                continue;

            var file = game.GetFile<GameMaterialFile>(path)!;
            var texture = GameMaterialLoader.BaseColorPath(file);

            texture.Should().NotBeNull(because: $"'{path}' binds a base color sampler");
            game.FileExists(texture!).Should().BeTrue(because: "the resolved texture path must exist in the archives");
            resolved++;
        }

        resolved.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConstantValue_DyeableFurnitureMaterial_ReadsItsDiffuseColor()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        const string Path = "bgcommon/hou/indoor/general/0681/material/fun_b0_m0681_0a.mtrl";
        if (!game.FileExists(Path))
        {
            Assert.Skip("Sample dyeable material not present.");
            return;
        }

        var file = game.GetFile<GameMaterialFile>(Path)!;
        var diffuse = file.ConstantValue("g_DiffuseColor");

        diffuse.Should().NotBeNull(because: "a dyeable material carries the constant its stain is written into");
        diffuse!.Length.Should().Be(3);
        foreach (var channel in diffuse)
            channel.Should().BeInRange(0f, 1f);

        // This dyeable piece sets a real color.
        diffuse.Should().NotBeEquivalentTo(new[] { 1f, 1f, 1f });
    }

    [Fact]
    public void Decode_BackgroundModel_CarriesBakedOcclusionInColorAlpha()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        const string Path = "bgcommon/hou/indoor/general/0681/bgparts/fun_b0_m0681.mdl";
        if (!game.FileExists(Path))
        {
            Assert.Skip("Sample model not present.");
            return;
        }

        // Background shaders write position.w into the G-buffer's occlusion channel. The importer carries it in the color's alpha.
        var meshes = GameModelLoader.Decode(game.GetFile<GameModelFile>(Path)!);
        var min = 1f;
        var max = 0f;
        foreach (var mesh in meshes)
        {
            foreach (var vertex in mesh.Geometry.Vertices)
            {
                min = Math.Min(min, vertex.Color.W);
                max = Math.Max(max, vertex.Color.W);
                vertex.Color.W.Should().BeInRange(0f, 1f);
            }
        }

        min.Should().BeLessThan(0.95f, because: "the stool's carved recesses carry baked occlusion");
        max.Should().BeGreaterThan(0.98f, because: "its open surfaces are fully unoccluded");
    }

    [Fact]
    public void DecodeTangentFrame_ReconstructsAnOrthogonalFrame()
    {
        // The shader recovers the bitangent as cross(normal, tangent) * handedness. For a +Y normal and +X bitangent that needs tangent = -Z.
        var normal = Vector3.UnitY;
        var packedBitangentX = new Vector4(1f, 0.5f, 0.5f, 1f); // bytes 255,128,128: bitangent +X, handedness +1

        var tangent = GameModelLoader.DecodeTangentFrame(packedBitangentX, normal);

        tangent.W.Should().Be(1f);
        var t = new Vector3(tangent.X, tangent.Y, tangent.Z);
        t.Length().Should().BeApproximately(1f, 1e-5f);
        Vector3.Dot(t, normal).Should().BeApproximately(0f, 1e-5f, because: "the frame is orthogonal");

        var rebuilt = Vector3.Cross(normal, t) * tangent.W;
        rebuilt.X.Should().BeApproximately(1f, 1e-4f);
        rebuilt.Y.Should().BeApproximately(0f, 1e-4f);
        rebuilt.Z.Should().BeApproximately(0f, 1e-4f);
    }

    [Fact]
    public void DecodeTangentFrame_DegenerateInputMeansNoFrame()
    {
        // 128,128,128 is a zero vector. The shaders key "no authored frame" on w == 0.
        GameModelLoader.DecodeTangentFrame(new Vector4(0.5f, 0.5f, 0.5f, 1f), Vector3.UnitY).Should().Be(Vector4.Zero);
    }

    [Fact]
    public void UndyedStain_IsTheStainTablesSnowWhite()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        // An empty stain slot renders stain row 1.
        var sheet = game.GetExcelSheet<Lumina.Excel.Sheets.Stain>();
        sheet.Should().NotBeNull();
        GameMaterial.UndyedStain.Should().Be(StainHelper.ToColor(sheet!.GetRow(1).Color));
    }

    [Theory]
    [InlineData("bgcommon/hou/indoor/general/0681/asset/fun_b0_m0681.sgb", 1)]
    [InlineData("bgcommon/hou/indoor/general/0560/asset/fun_b0_m0560.sgb", 14)]
    public void SceneFile_CarriesTheDefaultStain(string sgbPath, int expected)
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        if (!game.FileExists(sgbPath))
        {
            Assert.Skip("Sample sgb not present.");
            return;
        }

        // Verified in game: undyed, the second renders Blood Red and the first Snow White.
        StainHelper.TryReadSceneDefaultStain(game.GetFile(sgbPath)!.Data, out var stain).Should().BeTrue();
        ((int)stain).Should().Be(expected);
    }

    [Fact]
    public void SceneFile_ListsThePlacedModelWithItsTransform()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        const string Path = "bgcommon/hou/indoor/general/0681/asset/fun_b0_m0681.sgb";
        if (!game.FileExists(Path))
        {
            Assert.Skip("Sample sgb not present.");
            return;
        }

        // The entry names the model and its collision file with entry-relative offsets.
        var models = Models(LayerGroupHelper.Read(game.GetFile(Path)!.Data));
        models.Should().HaveCount(1);

        var placement = models[0];
        placement.AssetPath.Should().Be("bgcommon/hou/indoor/general/0681/bgparts/fun_b0_m0681.mdl");
        placement.CollisionPath.Should().EndWith(".pcb");
        placement.Translation.Should().Be(Vector3.Zero);
        placement.Scale.Should().Be(Vector3.One);
    }

    [Fact]
    public void SceneFile_MultiPartFurniturePlacesSeveralModels()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        const string Path = "bgcommon/hou/indoor/general/0116/asset/fun_b0_m0116.sgb";
        if (!game.FileExists(Path))
        {
            Assert.Skip("Sample sgb not present.");
            return;
        }

        var models = Models(LayerGroupHelper.Read(game.GetFile(Path)!.Data));
        models.Should().HaveCountGreaterThan(1);

        foreach (var placement in models)
        {
            placement.AssetPath.Should().EndWith(".mdl");
            placement.Scale.X.Should().BeInRange(0.001f, 1000f);
        }
    }

    [Fact]
    public void SceneFile_NestedScenesAreListedAsSharedGroups()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        const string Path = "bgcommon/hou/outdoor/general/0022/asset/gar_b0_m0022.sgb";
        if (!game.FileExists(Path))
        {
            Assert.Skip("Sample sgb not present.");
            return;
        }

        // Outdoor sets nest further scenes.
        var nested = new List<LayerGroupEntry>();
        foreach (var layer in LayerGroupHelper.Read(game.GetFile(Path)!.Data))
            nested.AddRange(layer.Entries.Where(e => e.Type == LayerEntryType.SharedGroup));
        nested.Should().NotBeEmpty();

        foreach (var group in nested)
        {
            group.AssetPath.Should().EndWith(".sgb");
            LayerGroupHelper.Read(game.GetFile(group.AssetPath)!.Data).Should().NotBeEmpty();
        }
    }

    [Fact]
    public void SceneFile_FlattenComposesNestedScenesOntoTheirParent()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        const string Path = "bgcommon/hou/outdoor/general/0022/asset/gar_b0_m0022.sgb";
        if (!game.FileExists(Path))
        {
            Assert.Skip("Sample sgb not present.");
            return;
        }

        IReadOnlyList<LayerGroupLayer> Read(string path) => game.GetFile(path) is { } file ? LayerGroupHelper.Read(file.Data) : [];

        // Child placement composed onto the parent's.
        var flat = LayerGroupHelper.Flatten(Path, Read, null, LayerGroupHelper.DefaultMaxDepth);
        var top = Read(Path).SelectMany(l => l.Entries).ToList();
        var expected = new List<(string Path, Matrix4x4 World)>();
        foreach (var entry in top)
        {
            expected.Add((entry.AssetPath, entry.World));
            if (entry.Type != LayerEntryType.SharedGroup)
                continue;
            foreach (var child in Read(entry.AssetPath).SelectMany(l => l.Entries))
                expected.Add((child.AssetPath, child.World * entry.World));
        }

        flat.Select(e => (e.AssetPath, e.World)).Should().Equal(expected);
        flat.Count.Should().BeGreaterThan(top.Count, "the nested scenes contribute their own models");
    }

    private static List<LayerGroupEntry> Models(IReadOnlyList<LayerGroupLayer> layers)
        => layers.SelectMany(l => l.Entries).Where(e => e.Type == LayerEntryType.BG).ToList();

    [Theory]
    [InlineData("bgcommon/hou/indoor/general/0681/material/fun_b0_m0681_0a.mtrl", 32u)]
    [InlineData("bgcommon/hou/indoor/general/0559/material/fun_b0_m0559_0a.mtrl", 14u)]
    public void DiffuseConstant_HoldsExactStainTableColors(string materialPath, uint stainRow)
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        if (!game.FileExists(materialPath))
        {
            Assert.Skip("Sample material not present.");
            return;
        }

        var file = game.GetFile<GameMaterialFile>(materialPath)!;
        var diffuse = file.ConstantValue("g_DiffuseColor");
        diffuse.Should().NotBeNull().And.HaveCount(3);

        var stain = StainHelper.ToColor(game.GetExcelSheet<Lumina.Excel.Sheets.Stain>()!.GetRow(stainRow).Color);
        var distance = (new Vector3(diffuse![0], diffuse[1], diffuse[2]) - stain).Length();
        distance.Should().BeLessThan(0.005f);
    }

    [Fact]
    public void EquipmentModel_EveryMaterialResolvesSomewhere()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        const string ModelPath = "chara/equipment/e0007/model/c0201e0007_top.mdl";
        if (!game.FileExists(ModelPath))
        {
            Assert.Skip("Sample equipment model not present.");
            return;
        }

        // The robe references its own material and its wearer's skin material under the human tree.
        var model = game.GetFile<GameModelFile>(ModelPath)!;
        model.MaterialPaths.Should().NotBeEmpty();

        foreach (var raw in model.MaterialPaths)
        {
            var resolved = GamePathHelper.ResolveMaterialPath(ModelPath, raw, variant: 10);
            if (resolved is not null && game.FileExists(resolved))
                continue;

            var found = false;
            foreach (var candidate in GamePathHelper.ResolveMaterialByOwnerName(raw, variant: 10))
                found |= game.FileExists(candidate);

            found.Should().BeTrue(because: $"'{raw}' must resolve somewhere, or its meshes draw with no material");
        }
    }

    [Fact]
    public void Decode_EquipmentModel_LandsAtTorsoHeight()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        const string Path = "chara/equipment/e0001/model/c0101e0001_top.mdl";
        if (!game.FileExists(Path))
        {
            Assert.Skip("Sample equipment model not present.");
            return;
        }

        var meshes = GameModelLoader.Decode(game.GetFile<GameModelFile>(Path)!);
        var lowest = float.MaxValue;
        var highest = float.MinValue;
        foreach (var mesh in meshes)
        {
            foreach (var vertex in mesh.Geometry.Vertices)
            {
                lowest = Math.Min(lowest, vertex.Position.Y);
                highest = Math.Max(highest, vertex.Position.Y);
            }
        }

        // Geometry centered on the origin would mean a misread vertex stride.
        lowest.Should().BeInRange(0.5f, 1.5f, because: "a torso garment starts around waist height");
        highest.Should().BeInRange(1.2f, 2.0f, because: "a torso garment ends around shoulder height");
    }

    /// <summary>Background models declare no vertex color element. Importing vertex colors cannot change how one is drawn.</summary>
    [Fact]
    public void Decode_BackgroundModels_CarryNoVertexColorChannel()
    {
        var game = GameDataFixture.TryOpen();
        if (game is null)
        {
            Assert.Skip("No game installation found.");
            return;
        }

        var checkedModels = 0;
        foreach (var path in SampleModels)
        {
            if (!path.StartsWith("bgcommon/", StringComparison.Ordinal) || !game.FileExists(path))
                continue;

            var file = game.GetFile<GameModelFile>(path);
            file.Should().NotBeNull();

            foreach (var declaration in file!.Declarations)
            {
                foreach (var element in declaration)
                {
                    element.Usage.Should().NotBe(
                        GameVertexUsage.Color,
                        because: $"'{path}' is a background model and should declare no vertex color");
                }
            }

            var without = GameModelLoader.Decode(file, 0, importVertexColors: false);
            var with = GameModelLoader.Decode(file, 0, importVertexColors: true);

            with.Length.Should().Be(without.Length);
            for (var i = 0; i < with.Length; i++)
            {
                with[i].Geometry.Vertices.Should().Equal(
                    without[i].Geometry.Vertices,
                    because: "a model with no color channel decodes identically whether or not colors are imported");
            }

            checkedModels++;
        }

        checkedModels.Should().BeGreaterThan(0, because: "the background sample paths should exist in any complete installation");
    }
}

// Opens the local game archives directly.
internal static class GameDataFixture
{
    private static GameData? cached;
    private static bool attempted;
    private static readonly object gate = new();

    /// <summary>Returns the local game archives, or null when no installation can be located.</summary>
    public static GameData? TryOpen()
    {
        // Test classes run in parallel and construction takes seconds.
        lock (gate)
        {
            if (attempted)
                return cached;

            attempted = true;

            foreach (var root in CandidateRoots())
            {
                var sqpack = Path.Combine(root, "game", "sqpack");
                if (!Directory.Exists(sqpack))
                    continue;

                try
                {
                    cached = new GameData(sqpack);
                    return cached;
                }
                catch
                {
                    // An unreadable installation counts as absent.
                }
            }

            return null;
        }
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var config = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher",
            "launcherConfigV3.json");

        if (File.Exists(config))
        {
            var match = Regex.Match(
                File.ReadAllText(config),
                "\"GamePath\"\\s*:\\s*\"(?<path>(?:[^\"\\\\]|\\\\.)*)\"");

            if (match.Success)
                yield return match.Groups["path"].Value.Replace("\\\\", "\\");
        }

        yield return @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn";
        yield return @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online";
    }
}
