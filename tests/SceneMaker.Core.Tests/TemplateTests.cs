using SceneMaker.Core;
using Xunit;

namespace SceneMaker.Core.Tests;

public sealed class TemplateTests
{
    [Fact]
    public void SceneKindsRoundTripWithExplicitOppositeNullability()
    {
        var instance = SceneDocument.Create("world", 20, 12, SceneKind.Instance);
        var template = SceneDocument.Create(
            "forest_patch",
            8,
            6,
            SceneKind.Template,
            templateGroupNumber: 4,
            insertionAnchorX: 32,
            insertionAnchorY: 16);

        var instanceJson = DocumentJson.Serialize(instance);
        var templateJson = DocumentJson.Serialize(template);
        Assert.Contains("\"scene_kind\": \"instance\"", instanceJson);
        Assert.Contains("\"template_definition\": null", instanceJson);
        Assert.Contains("\"scene_kind\": \"template\"", templateJson);
        Assert.Equal(
            instanceJson,
            DocumentJson.Serialize(DocumentJson.DeserializeScene(instanceJson)));
        Assert.Equal(
            templateJson,
            DocumentJson.Serialize(DocumentJson.DeserializeScene(templateJson)));
        Assert.Equal(4, template.TemplateDefinition!.GroupNumber);
        Assert.Equal(32, template.TemplateDefinition.InsertionAnchorAuthoringPx.X);
        Assert.Equal(16, template.TemplateDefinition.InsertionAnchorAuthoringPx.Y);
    }

    [Fact]
    public void KindSpecificDataIsStrictlySeparated()
    {
        var instanceWithDefinition = SceneDocument.Create("world", 2, 2) with
        {
            TemplateDefinition = new TemplateDefinitionDocument
            {
                GroupNumber = 1,
                InsertionAnchorAuthoringPx = new AuthoringPixelPosition { X = 0, Y = 0 },
            },
        };
        var templateWithAnchor = SceneDocument.Create(
            "patch",
            2,
            2,
            SceneKind.Template) with
        {
            TemplateAnchors =
            [
                new TemplateAnchorDocument
                {
                    AnchorId = "template_anchor_001",
                    GroupNumber = 1,
                    PositionAuthoringPx = new AuthoringPixelPosition { X = 16, Y = 16 },
                },
            ],
        };

        Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(instanceWithDefinition));
        Assert.Throws<SceneMakerDocumentException>(() =>
            DocumentValidation.Validate(templateWithAnchor));
    }

    [Fact]
    public void InstanceAnchorsSnapMoveAndChangeGroupWithoutChangingSceneContent()
    {
        var original = SceneDocument.Create("world", 10, 10);
        var placed = TemplateEditing.PlaceAnchor(original, 23, 39, groupNumber: 2);
        var anchor = Assert.Single(placed.TemplateAnchors);

        Assert.Equal(32, TemplateEditing.AnchorVisualSizeAuthoringPixels);
        Assert.Equal("template_anchor_001", anchor.AnchorId);
        Assert.Equal(16, anchor.PositionAuthoringPx.X);
        Assert.Equal(32, anchor.PositionAuthoringPx.Y);
        Assert.Empty(placed.TerrainCells);
        Assert.Empty(placed.Placements);
        Assert.Empty(placed.Transitions);

        var moved = TemplateEditing.MoveAnchor(placed, anchor.AnchorId, 155, 145);
        moved = TemplateEditing.SetAnchorGroup(moved, anchor.AnchorId, 7);
        anchor = Assert.Single(moved.TemplateAnchors);
        Assert.Equal(160, anchor.PositionAuthoringPx.X);
        Assert.Equal(144, anchor.PositionAuthoringPx.Y);
        Assert.Equal(7, anchor.GroupNumber);
        Assert.Equal(anchor, TemplateEditing.FindAnchorAt(moved, 159, 143));
    }

    [Fact]
    public void GridAnchorsRejectInvalidGroupsAndOutOfBoundsPositions()
    {
        var instance = SceneDocument.Create("world", 2, 2);
        var template = SceneDocument.Create("patch", 2, 2, SceneKind.Template);

        Assert.Throws<SceneMakerDocumentException>(() =>
            TemplateEditing.PlaceAnchor(instance, 64, 0, 0));
        Assert.Throws<SceneMakerDocumentException>(() =>
            TemplateEditing.PlaceAnchor(instance, 65, 0, 1));
        Assert.Throws<SceneMakerDocumentException>(() =>
            TemplateEditing.PlaceAnchor(template, 16, 16, 1));
        Assert.Throws<SceneMakerDocumentException>(() =>
            TemplateEditing.SetTemplateGroup(template, 0));
    }

    [Fact]
    public void TemplateInsertionAnchorMustUseWorldGridIntersections()
    {
        var offGrid = SceneDocument.Create(
            "patch",
            4,
            4,
            SceneKind.Template,
            insertionAnchorX: 1,
            insertionAnchorY: 16);
        var outside = SceneDocument.Create(
            "patch",
            4,
            4,
            SceneKind.Template,
            insertionAnchorX: 80,
            insertionAnchorY: 16);

        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(offGrid));
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(outside));

        var moved = TemplateEditing.MoveTemplateInsertionAnchor(
            SceneDocument.Create("valid_patch", 4, 4, SceneKind.Template),
            31,
            47);
        Assert.Equal(32, moved.TemplateDefinition!.InsertionAnchorAuthoringPx.X);
        Assert.Equal(48, moved.TemplateDefinition.InsertionAnchorAuthoringPx.Y);
    }
}
