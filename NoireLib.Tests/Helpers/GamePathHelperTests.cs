using FluentAssertions;
using NoireLib.Helpers;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the archive path conventions: where a model's material resolves to, where a character material's owner puts
/// it, where the DirectX 11 texture sits, and which scene pairs with a furniture model.
/// </summary>
public class GamePathHelperTests
{
    private const string EquipmentModel = "chara/equipment/e0001/model/c0101e0001_top.mdl";

    [Fact]
    public void ResolveMaterialPath_AbsolutePath_IsReturnedUnchanged()
        => GamePathHelper.ResolveMaterialPath(EquipmentModel, "bg/ffxiv/fst_f1/common/material/x.mtrl")
            .Should().Be("bg/ffxiv/fst_f1/common/material/x.mtrl");

    [Fact]
    public void ResolveMaterialPath_RelativePath_ResolvesBesideTheModelFolder()
        => GamePathHelper.ResolveMaterialPath(EquipmentModel, "/mt_c0101e0001_top_a.mtrl", variant: 10)
            .Should().Be("chara/equipment/e0001/material/v0010/mt_c0101e0001_top_a.mtrl",
                "the model's own folder is dropped, since a character material sits beside it rather than under it");

    [Fact]
    public void ResolveMaterialPath_EmptyMaterial_IsNull()
        => GamePathHelper.ResolveMaterialPath(EquipmentModel, "   ").Should().BeNull();

    [Fact]
    public void ResolveMaterialPath_ModelWithoutEnoughFolders_IsNull()
        => GamePathHelper.ResolveMaterialPath("model.mdl", "/mt_x.mtrl").Should().BeNull();

    [Fact]
    public void ResolveMaterialByOwnerName_HumanKind_OffersBothVariantLayouts()
        => GamePathHelper.ResolveMaterialByOwnerName("/mt_c0201b0001_a.mtrl").Should().Equal(
            "chara/human/c0201/obj/body/b0001/material/v0001/mt_c0201b0001_a.mtrl",
            "chara/human/c0201/obj/body/b0001/material/mt_c0201b0001_a.mtrl");

    [Fact]
    public void ResolveMaterialByOwnerName_EquipmentKind_UsesTheVariantFolder()
        => GamePathHelper.ResolveMaterialByOwnerName("/mt_c0201e0007_top_a.mtrl", variant: 10).Should().Equal(
            "chara/equipment/e0007/material/v0010/mt_c0201e0007_top_a.mtrl");

    [Fact]
    public void ResolveMaterialByOwnerName_NamesThatAreNotCharacterMaterials_AreEmpty()
    {
        GamePathHelper.ResolveMaterialByOwnerName("/mt_notacharacter.mtrl").Should().BeEmpty();
        GamePathHelper.ResolveMaterialByOwnerName("bg/absolute/path.mtrl").Should().BeEmpty("an absolute path names no owner");
        GamePathHelper.ResolveMaterialByOwnerName("/mt_cABCDb0001_a.mtrl").Should().BeEmpty("the character segment must be digits");
        GamePathHelper.ResolveMaterialByOwnerName("/mt_c0201x0001_a.mtrl").Should().BeEmpty("x is not a known kind");
    }

    [Fact]
    public void Dx11TexturePath_PrefixesTheFileNameNotTheFolder()
    {
        GamePathHelper.Dx11TexturePath("chara/equipment/e0001/texture/v01_c0101e0001_top_d.tex")
            .Should().Be("chara/equipment/e0001/texture/--v01_c0101e0001_top_d.tex");

        GamePathHelper.Dx11TexturePath("bare.tex").Should().Be("--bare.tex");
        GamePathHelper.Dx11TexturePath(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void SceneBesideModel_PairsFurnitureWithItsScene()
    {
        GamePathHelper.SceneBesideModel("bgcommon/hou/indoor/general/0681/bgparts/fun_b0_m0681.mdl")
            .Should().Be("bgcommon/hou/indoor/general/0681/asset/fun_b0_m0681.sgb");

        GamePathHelper.SceneBesideModel(EquipmentModel).Should().BeNull("only bgparts models pair with a scene");
        GamePathHelper.SceneBesideModel("not-a-model.tex").Should().BeNull();
        GamePathHelper.SceneBesideModel(" ").Should().BeNull();
    }
}
