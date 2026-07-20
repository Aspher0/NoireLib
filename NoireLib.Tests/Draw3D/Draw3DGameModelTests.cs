using FluentAssertions;
using Lumina;
using NoireLib.Draw3D.Assets;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the game model parser against real game files: the layout walk must land exactly on the
/// declared runtime block size, and the decoded geometry must be finite and plausibly sized.<br/>
/// These tests need a local game installation and skip cleanly without one, so they are a
/// correctness gate on machines that have the game and inert everywhere else.
/// </summary>
public class Draw3DGameModelTests
{
    /// <summary>Models chosen to cover both position encodings, both material path styles, and a range of sizes.</summary>
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

            // A mis-sized block anywhere in the runtime walk makes LoadFile throw, because every
            // buffer offset derived from it would silently address the wrong bytes.
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

    /// <summary>Materials covering both shader packages and both color table layouts.</summary>
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

            // A mis-sized block makes LoadFile throw, because the walk must end on the declared file size.
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
                // Sampler identifiers are a checksum of the sampler's name, so a shipped name list
                // reproduces them exactly. A miss means either the derivation or the list has drifted.
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

        // The constant is parsed data: it is a slot the stain system writes into, and its file value is not
        // what an undyed item renders as (that is the default stain - see UndyedStain_IsTheGamesSnowWhite).
        // Reading it still has to work, which is what this locks.
        diffuse.Should().NotBeNull(because: "a dyeable material carries the constant its stain is written into");
        diffuse!.Length.Should().Be(3);
        foreach (var channel in diffuse)
            channel.Should().BeInRange(0f, 1f);

        // Most materials leave this white, where it changes nothing. This one is a dyeable piece and sets a
        // real color, which is what makes it a useful sample: an all-white read would mean the value was missed.
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

        // The game's own background shaders write the position element's fourth component into the
        // G-buffer's occlusion channel; the importer carries it in the color's alpha. A carved piece must
        // therefore decode with alpha varying below 1 in its recesses while its open surfaces sit at 1 -
        // a flat 1 everywhere would mean the component was dropped again.
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
        // The model stores the bitangent with a handedness flag; the loader reconstructs the tangent so the
        // shader's bitangent = cross(normal, tangent) * handedness lands back on what the file stored. For a
        // +Y normal and a +X bitangent with positive handedness, the round trip demands tangent = -Z... the
        // assertion below IS that algebra, so a sign change in the reconstruction fails here before it ships
        // as a model whose bumps face the wrong way.
        var normal = Vector3.UnitY;
        var packedBitangentX = new Vector4(1f, 0.5f, 0.5f, 1f); // bytes 255,128,128 -> bitangent +X, handedness +1

        var tangent = GameModelLoader.DecodeTangentFrame(packedBitangentX, normal);

        tangent.W.Should().Be(1f);
        var t = new Vector3(tangent.X, tangent.Y, tangent.Z);
        t.Length().Should().BeApproximately(1f, 1e-5f);
        Vector3.Dot(t, normal).Should().BeApproximately(0f, 1e-5f, because: "the frame is orthogonal");

        // The shader's reconstruction of the bitangent must return the stored vector.
        var rebuilt = Vector3.Cross(normal, t) * tangent.W;
        rebuilt.X.Should().BeApproximately(1f, 1e-4f);
        rebuilt.Y.Should().BeApproximately(0f, 1e-4f);
        rebuilt.Z.Should().BeApproximately(0f, 1e-4f);
    }

    [Fact]
    public void DecodeTangentFrame_DegenerateInputMeansNoFrame()
    {
        // Bytes at 128,128,128 encode a zero vector; the shaders key "no authored frame" on w == 0, so the
        // decode must return exactly zero rather than a normalize() of noise.
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

        // An empty stain slot renders stain row 1; this pins the shipped constant to the game's own table so
        // a patch that moves the table breaks here instead of silently shifting every undyed item.
        var sheet = game.GetExcelSheet<Lumina.Excel.Sheets.Stain>();
        sheet.Should().NotBeNull();
        GameMaterial.UndyedStain.Should().Be(StainHelper.ToColor(sheet!.GetRow(1).Color));
    }

    [Theory]
    [InlineData("bgcommon/hou/indoor/general/0681/asset/fun_b0_m0681.sgb", 1)]  // Snow White, stated explicitly - the bar stool's known undyed look
    [InlineData("bgcommon/hou/indoor/general/0560/asset/fun_b0_m0560.sgb", 14)] // Blood Red - the Hingan Sofa's known undyed look
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

        // Both anchors were verified in game: an undyed placement of the second renders Blood Red, of the
        // first Snow White, with the stain slot empty in both cases. The value is stated in the file for
        // both - dyeable furniture names its default stain rather than relying on a fallback.
        ((int)game.GetFile<GameSgbFile>(sgbPath)!.DefaultStain).Should().Be(expected);
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

        // Single-model furniture places its one model at the scene origin, and the entry names both the
        // model and its collision file with entry-relative offsets - the walk this pins.
        var scene = game.GetFile<GameSgbFile>(Path)!;
        scene.Models.Should().HaveCount(1);

        var placement = scene.Models[0];
        placement.Path.Should().Be("bgcommon/hou/indoor/general/0681/bgparts/fun_b0_m0681.mdl");
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

        // An item made of several models must decode them all, each with a plausible transform - this is
        // what spawning the model file alone was silently missing.
        var scene = game.GetFile<GameSgbFile>(Path)!;
        scene.Models.Should().HaveCountGreaterThan(1);

        foreach (var placement in scene.Models)
        {
            placement.Path.Should().EndWith(".mdl");
            placement.Scale.X.Should().BeInRange(0.001f, 1000f);
        }
    }

    [Fact]
    public void SceneFile_NestedScenesAreListedAsAttachments()
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

        // Outdoor sets nest further scenes; every referenced file must itself parse with this layout so a
        // recursive load cannot walk into an unreadable file.
        var scene = game.GetFile<GameSgbFile>(Path)!;
        scene.Attachments.Should().NotBeEmpty();

        foreach (var attachment in scene.Attachments)
        {
            attachment.Path.Should().EndWith(".sgb");
            game.GetFile<GameSgbFile>(attachment.Path).Should().NotBeNull();
        }
    }

    [Fact]
    public void PathBesideModel_PairsFurnitureModelsWithTheirScene()
    {
        GameSgbFile.PathBesideModel("bgcommon/hou/indoor/general/0681/bgparts/fun_b0_m0681.mdl")
            .Should().Be("bgcommon/hou/indoor/general/0681/asset/fun_b0_m0681.sgb");

        GameSgbFile.PathBesideModel("chara/equipment/e0001/model/c0101e0001_top.mdl").Should().BeNull();
        GameSgbFile.PathBesideModel("not-a-model.tex").Should().BeNull();
    }

    [Theory]
    [InlineData("bgcommon/hou/indoor/general/0681/material/fun_b0_m0681_0a.mtrl", 32u)] // Mole Brown
    [InlineData("bgcommon/hou/indoor/general/0559/material/fun_b0_m0559_0a.mtrl", 14u)] // Blood Red
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

        // The constant lands on stain rows verbatim across dyeable furniture, but it is not what an undyed
        // placement renders (that is the sgb's default stain, or the Snow White fallback). Pinned so a layout
        // change in either the parser or the table is caught; its in-game role is documented in docs/.
        var file = game.GetFile<GameMaterialFile>(materialPath)!;
        var diffuse = file.ConstantValue("g_DiffuseColor");
        diffuse.Should().NotBeNull().And.HaveCount(3);

        var stain = StainHelper.ToColor(game.GetExcelSheet<Lumina.Excel.Sheets.Stain>()!.GetRow(stainRow).Color);
        var distance = (new Vector3(diffuse![0], diffuse[1], diffuse[2]) - stain).Length();
        distance.Should().BeLessThan(0.005f);
    }

    [Fact]
    public void ResolveByOwnerName_ReadsTheOwnerOutOfTheFilename()
    {
        // A body material referenced by an equipment model lives under the human tree, and only the filename
        // says so. Both variant layouts are offered because the human directories split on it.
        GameMaterialLoader.ResolveByOwnerName("/mt_c0201b0001_a.mtrl").Should().Equal(
            "chara/human/c0201/obj/body/b0001/material/v0001/mt_c0201b0001_a.mtrl",
            "chara/human/c0201/obj/body/b0001/material/mt_c0201b0001_a.mtrl");

        GameMaterialLoader.ResolveByOwnerName("/mt_c0201e0007_top_a.mtrl", variant: 10).Should().Equal(
            "chara/equipment/e0007/material/v0010/mt_c0201e0007_top_a.mtrl");

        GameMaterialLoader.ResolveByOwnerName("/mt_notacharacter.mtrl").Should().BeEmpty();
        GameMaterialLoader.ResolveByOwnerName("bg/absolute/path.mtrl").Should().BeEmpty();
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

        // The robe references its own material AND its wearer's skin material. The skin one does not resolve
        // beside the model - its file lives under the human tree - which is exactly what left a garment's
        // body parts drawing with no material until the owner-name fallback existed.
        var model = game.GetFile<GameModelFile>(ModelPath)!;
        model.MaterialPaths.Should().NotBeEmpty();

        foreach (var raw in model.MaterialPaths)
        {
            var resolved = GameMaterialLoader.ResolvePath(ModelPath, raw, variant: 10);
            if (resolved is not null && game.FileExists(resolved))
                continue;

            var found = false;
            foreach (var candidate in GameMaterialLoader.ResolveByOwnerName(raw, variant: 10))
                found |= game.FileExists(candidate);

            found.Should().BeTrue(because: $"'{raw}' must resolve somewhere, or its meshes draw with no material");
        }
    }

    [Fact]
    public void ResolvePath_RelativeCharacterMaterial_LandsOnTheVariantFolder()
    {
        // Character models store material paths relative, and they resolve beside the model directory
        // rather than under it. Getting this wrong yields a path that simply does not exist.
        var resolved = GameMaterialLoader.ResolvePath(
            "chara/equipment/e0001/model/c0101e0001_top.mdl",
            "/mt_c0101e0001_top_a.mtrl");

        resolved.Should().Be("chara/equipment/e0001/material/v0001/mt_c0101e0001_top_a.mtrl");
    }

    [Fact]
    public void ResolvePath_AbsoluteBackgroundMaterial_IsUnchanged()
    {
        const string Absolute = "bgcommon/hou/indoor/general/0001/material/fun_b0_m0001_1a.mtrl";

        GameMaterialLoader.ResolvePath("bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl", Absolute)
            .Should().Be(Absolute);
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

        // A body garment sits on the torso of a character roughly 1.8 units tall. Geometry centred on
        // the origin instead would mean the vertex stride was misread and every offset drifted.
        lowest.Should().BeInRange(0.5f, 1.5f, because: "a torso garment starts around waist height");
        highest.Should().BeInRange(1.2f, 2.0f, because: "a torso garment ends around shoulder height");
    }

    /// <summary>
    /// Background models declare no vertex color element, so importing vertex colors cannot change how one
    /// is drawn. This is worth pinning down because the option is visible next to settings that do change
    /// the result, which invites reading a coincidence as cause and effect.
    /// </summary>
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

            // The decoder must then produce the same geometry either way, since there is nothing to import.
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

/// <summary>Opens the local game archives directly, so model parsing can be tested without Dalamud or a running game.</summary>
internal static class GameDataFixture
{
    private static GameData? cached;
    private static bool attempted;

    /// <summary>Returns the local game archives, or null when no installation can be located.</summary>
    public static GameData? TryOpen()
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
                // An unreadable installation is treated as absent; these tests are a gate, not a hard requirement.
            }
        }

        return null;
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
