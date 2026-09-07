using System.Text.Json;
using SceneMaker.TestSupport;
using Xunit;

namespace SceneMaker.Core.Tests;

/// <summary>
/// A branch is its own water body, and a state decides which bodies a Scene has.
///
/// <para>These pin the two halves of that: what an author is allowed to write
/// down, and what a consumer receives for it. The geometry itself is not
/// retested here - a branch is an ordinary river and goes through the corridor
/// rule every other river goes through.</para>
/// </summary>
public sealed class WaterActivationTests
{
    /// <summary>
    /// The parent runs east along y = 32 and the branch leaves it halfway, at
    /// the point (96, 32) that lies exactly on its centerline: two authoring
    /// metres from the source, which is the station the export has to work out.
    /// </summary>
    private static SceneDocument WithFork(SceneDocument scene, TestWorkspace workspace)
    {
        scene = WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");
        scene = WaterEditing.PlaceRiver(
            scene,
            workspace.Terrain,
            [
                WaterEditing.Point(96, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(96, 160, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");

        return scene with
        {
            ActivationGroups =
            [
                new ActivationGroupDocument
                {
                    Group = "fork_at_mill",
                    States = ["dry", "flowing"],
                    InitialState = "dry",
                },
            ],
            WaterBodies =
            [
                scene.WaterBodies[0],
                scene.WaterBodies[1] with
                {
                    Activation = new WaterActivationDocument
                    {
                        Group = "fork_at_mill",
                        ActiveIn = ["flowing"],
                        Inactive = WaterInactive.DryBed,
                    },
                    Junctions =
                    [
                        new WaterJunctionDocument
                        {
                            End = WaterEnd.Source,
                            WaterBodyId = "river_0001",
                        },
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// One authored statement, two exported entries. A consumer holding either
    /// body learns where its water goes without scanning the file, and the two
    /// sides cannot disagree because only one of them was written down.
    /// </summary>
    [Fact]
    public void AForkIsExportedOnBothBodiesWithTheStationsExchanged()
    {
        using var workspace = TestWorkspace.Create();
        var raster = Export(workspace, WithFork).GetProperty("water_raster");

        var parent = raster[0];
        var branch = raster[1];
        Assert.Equal("river_0001", parent.GetProperty("water_body_id").GetString());
        Assert.Equal("river_0002", branch.GetProperty("water_body_id").GetString());

        var onParent = Assert.Single(parent.GetProperty("junctions").EnumerateArray());
        Assert.Equal(
            ["water_body_id", "own_station_meters", "station_meters"],
            onParent.EnumerateObject().Select(property => property.Name));
        Assert.Equal("river_0002", onParent.GetProperty("water_body_id").GetString());
        Assert.Equal(2.0m, onParent.GetProperty("own_station_meters").GetDecimal());
        Assert.Equal(0.0m, onParent.GetProperty("station_meters").GetDecimal());

        var onBranch = Assert.Single(branch.GetProperty("junctions").EnumerateArray());
        Assert.Equal("river_0001", onBranch.GetProperty("water_body_id").GetString());
        Assert.Equal(0.0m, onBranch.GetProperty("own_station_meters").GetDecimal());
        Assert.Equal(2.0m, onBranch.GetProperty("station_meters").GetDecimal());
    }

    /// <summary>
    /// The shape an author is told to use before a narrowing exists: the main
    /// river split at the fork, so that the fork is an authored point of three
    /// curves and the ids stay stable when the second width arrives.
    ///
    /// <para>It is also the awkward case for the junction rule. The branch's
    /// source does not sit somewhere along the parent - it sits exactly on the
    /// parent's last point, on the square cap the corridor ends with. If that
    /// boundary did not count as inside, the recommended way to author a fork
    /// would be the one way that cannot be exported.</para>
    /// </summary>
    [Fact]
    public void ASplitRiverAndItsBranchAllMeetOnTheParentsLastPoint()
    {
        using var workspace = TestWorkspace.Create();
        var raster = Export(workspace, (scene, work) =>
        {
            foreach (var (fromX, fromY, toX, toY) in new[]
                     {
                         (32, 32, 96, 32),    // river_0001, source to the fork
                         (96, 32, 160, 32),   // river_0002, the fork onwards
                         (96, 32, 96, 160),   // river_0003, the branch
                     })
            {
                scene = WaterEditing.PlaceRiver(
                    scene,
                    work.Terrain,
                    [
                        WaterEditing.Point(fromX, fromY, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                        WaterEditing.Point(toX, toY, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                    ],
                    "river");
            }

            // Both children name the body the water arrives from, which is what
            // lets a consumer split what travels down it at the fork.
            List<WaterJunctionDocument> fedByTheUpperRiver =
                [new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = "river_0001" }];
            return scene with
            {
                WaterBodies =
                [
                    scene.WaterBodies[0],
                    scene.WaterBodies[1] with { Junctions = fedByTheUpperRiver },
                    scene.WaterBodies[2] with { Junctions = fedByTheUpperRiver },
                ],
            };
        }).GetProperty("water_raster");

        // The upper river carries both meetings, at its own mouth: two
        // authored statements, and this is the side neither of them was made on.
        var upstream = raster[0].GetProperty("junctions").EnumerateArray().ToList();
        Assert.Equal(2, upstream.Count);
        Assert.Equal(
            ["river_0002", "river_0003"],
            upstream.Select(meeting => meeting.GetProperty("water_body_id").GetString()));
        Assert.All(upstream, meeting =>
        {
            Assert.Equal(2.0m, meeting.GetProperty("own_station_meters").GetDecimal());
            Assert.Equal(0.0m, meeting.GetProperty("station_meters").GetDecimal());
        });

        foreach (var child in new[] { raster[1], raster[2] })
        {
            var meeting = Assert.Single(child.GetProperty("junctions").EnumerateArray());
            Assert.Equal("river_0001", meeting.GetProperty("water_body_id").GetString());
            Assert.Equal(0.0m, meeting.GetProperty("own_station_meters").GetDecimal());
            Assert.Equal(2.0m, meeting.GetProperty("station_meters").GetDecimal());
        }
    }

    /// <summary>
    /// The group is authored, so it belongs in the Scene block; the raster
    /// repeats a body's own activation so that a consumer reading the derived
    /// water need not join three arrays to learn whether to apply one of them.
    /// </summary>
    [Fact]
    public void AGroupIsAuthoredInTheSceneAndABodySaysWhichStatesItExistsIn()
    {
        using var workspace = TestWorkspace.Create();
        var root = Export(workspace, WithFork);

        var group = Assert.Single(
            root.GetProperty("scene").GetProperty("activation_groups").EnumerateArray());
        Assert.Equal(
            ["group", "states", "initial_state"],
            group.EnumerateObject().Select(property => property.Name));
        Assert.Equal("fork_at_mill", group.GetProperty("group").GetString());
        Assert.Equal("dry", group.GetProperty("initial_state").GetString());
        Assert.Equal(
            ["dry", "flowing"],
            group.GetProperty("states").EnumerateArray().Select(state => state.GetString()));

        foreach (var activation in new[]
                 {
                     root.GetProperty("scene").GetProperty("water_bodies")[1].GetProperty("activation"),
                     root.GetProperty("water_raster")[1].GetProperty("activation"),
                 })
        {
            Assert.Equal(
                ["group", "active_in", "inactive"],
                activation.EnumerateObject().Select(property => property.Name));
            Assert.Equal("fork_at_mill", activation.GetProperty("group").GetString());
            Assert.Equal("dry_bed", activation.GetProperty("inactive").GetString());
            Assert.Equal(
                ["flowing"],
                activation.GetProperty("active_in").EnumerateArray().Select(state => state.GetString()));
        }

        // The parent exists whatever the state, and says so by saying nothing.
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("water_raster")[0].GetProperty("activation").ValueKind);
    }

    /// <summary>
    /// The hole the first draft of this design had: with two branches on one
    /// stretch, one of them flows in more than one state. A single state per
    /// body would force it to be authored twice with identical curves, which is
    /// the duplication of geometry a per-body band exists to avoid.
    /// </summary>
    [Fact]
    public void ABodyMayBeActiveInMoreThanOneState()
    {
        var scene = Fixture() with
        {
            ActivationGroups =
            [
                new ActivationGroupDocument
                {
                    Group = "two_forks",
                    States = ["none", "first", "both"],
                    InitialState = "none",
                },
            ],
        };
        scene = WithActivation(scene, "two_forks", ["first", "both"]);

        DocumentValidation.Validate(scene);
        Assert.Equal(["first", "both"], scene.WaterBodies[0].Activation!.ActiveIn);
    }

    [Fact]
    public void AGroupTheSceneDoesNotDeclareIsRefused()
    {
        var scene = WithActivation(Fixture(), "no_such_group", ["flowing"]);
        var error = Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(scene));
        Assert.Contains("no_such_group", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStateThatIsNotTheGroupsIsRefused()
    {
        var scene = WithGroup(Fixture());
        scene = WithActivation(scene, "fork_at_mill", ["flooded"]);
        var error = Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(scene));
        Assert.Contains("flooded", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A body active in no state is a body nobody can ever see. It is refused
    /// rather than exported, because an empty list reads as "not switched" and
    /// means the opposite.
    /// </summary>
    [Fact]
    public void ABodyActiveInNoStateIsRefused()
    {
        var scene = WithActivation(WithGroup(Fixture()), "fork_at_mill", []);
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(scene));
    }

    [Fact]
    public void AGroupWithOneStateSwitchesNothingAndIsRefused()
    {
        var scene = Fixture() with
        {
            ActivationGroups =
            [
                new ActivationGroupDocument
                {
                    Group = "fork_at_mill",
                    States = ["dry"],
                    InitialState = "dry",
                },
            ],
        };
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(scene));
    }

    [Fact]
    public void AnInitialStateOutsideTheGroupIsRefused()
    {
        var scene = Fixture() with
        {
            ActivationGroups =
            [
                new ActivationGroupDocument
                {
                    Group = "fork_at_mill",
                    States = ["dry", "flowing"],
                    InitialState = "flooded",
                },
            ],
        };
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(scene));
    }

    [Fact]
    public void ABodyCannotMeetItselfOrABodyTheSceneDoesNotHave()
    {
        var itself = WithJunction(Fixture(), "river_0001");
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(itself));

        var stranger = WithJunction(Fixture(), "river_0404");
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(stranger));
    }

    /// <summary>
    /// A Template carries no water, so a group in one would be a name a
    /// consumer could bind a trigger to that switches nothing.
    /// </summary>
    [Fact]
    public void ATemplateCannotOwnActivationGroups()
    {
        using var workspace = TestWorkspace.Create();
        var template = TestScenes.Template(workspace, "grove", 1) with
        {
            ActivationGroups =
            [
                new ActivationGroupDocument
                {
                    Group = "fork_at_mill",
                    States = ["dry", "flowing"],
                    InitialState = "dry",
                },
            ],
        };
        Assert.Throws<SceneMakerDocumentException>(() => DocumentValidation.Validate(template));
    }

    /// <summary>
    /// The rule world01 gave us: a bed is deeper than a character can climb, so
    /// a river is not crossable in either state. That number is a fact about a
    /// World and not about authoring, so it lives in the Workspace and this
    /// only holds an author to what the World wrote down.
    /// </summary>
    [Fact]
    public void AChannelShallowerThanTheWorkspaceAsksForIsRefused()
    {
        using var workspace = TestWorkspace.Create();
        var scene = WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.1m, 5.0m, 1.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 0.1m, 5.0m, 1.0m),
            ],
            "river");

        var error = Assert.Throws<SceneMakerDocumentException>(
            () => DocumentValidation.ValidateGrid(scene, workspace.Metrics));
        Assert.Contains("0.1 m deep", error.Message, StringComparison.Ordinal);
        Assert.Contains("at least", error.Message, StringComparison.Ordinal);

        // The Workspace minimum, and anything past it, is fine.
        var deepEnough = WaterEditing.Reshape(
            scene,
            "river_0001",
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.25m, 5.0m, 1.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 1.0m, 5.0m, 1.0m),
            ]);
        DocumentValidation.ValidateGrid(deepEnough, workspace.Metrics);
    }

    /// <summary>
    /// The gap that let a branch quietly stop being switchable: the group stayed
    /// declared, nothing was in it, and the file exported without a word.
    /// </summary>
    [Fact]
    public void AGroupNoBodyIsInIsReportedRatherThanExportedInSilence()
    {
        using var workspace = TestWorkspace.Create();
        var warnings = Warnings(workspace, (scene, work) => WithFork(scene, work) with
        {
            WaterBodies = [.. WithFork(scene, work).WaterBodies.Select(
                static body => body with { Activation = null, Junctions = [] })],
        });

        Assert.Contains(
            warnings,
            warning => warning.Contains("fork_at_mill", StringComparison.Ordinal)
                && warning.Contains("nothing switches", StringComparison.Ordinal));

        // With the branch back in it, there is nothing to say.
        Assert.DoesNotContain(
            Warnings(workspace, WithFork),
            warning => warning.Contains("fork_at_mill", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Warnings(
        TestWorkspace workspace,
        Func<SceneDocument, TestWorkspace, SceneDocument> extend)
    {
        var session = WorkspaceSession.Load(workspace.RootPath);
        var scene = new LoadedScene(
            Path.Combine(session.Workspace.ScenesDirectoryPath, "base.scene.json"),
            extend(TestScenes.Instance(workspace), workspace));
        return SceneExport.Write(session, scene).Warnings;
    }

    private static SceneDocument Fixture()
    {
        using var workspace = TestWorkspace.Create();
        return WaterEditing.PlaceRiver(
            TestScenes.Instance(workspace),
            workspace.Terrain,
            [
                WaterEditing.Point(32, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
                WaterEditing.Point(160, 32, WaterPointMode.Linear, 2.0m, 0.5m, 5.0m, 1.0m),
            ],
            "river");
    }

    private static SceneDocument WithGroup(SceneDocument scene) => scene with
    {
        ActivationGroups =
        [
            new ActivationGroupDocument
            {
                Group = "fork_at_mill",
                States = ["dry", "flowing"],
                InitialState = "dry",
            },
        ],
    };

    private static SceneDocument WithActivation(
        SceneDocument scene,
        string group,
        List<string> activeIn) => scene with
    {
        WaterBodies =
        [
            scene.WaterBodies[0] with
            {
                Activation = new WaterActivationDocument
                {
                    Group = group,
                    ActiveIn = activeIn,
                    Inactive = WaterInactive.DryBed,
                },
            },
        ],
    };

    private static SceneDocument WithJunction(SceneDocument scene, string partner) => scene with
    {
        WaterBodies =
        [
            scene.WaterBodies[0] with
            {
                Junctions =
                [
                    new WaterJunctionDocument { End = WaterEnd.Source, WaterBodyId = partner },
                ],
            },
        ],
    };

    private static JsonElement Export(
        TestWorkspace workspace,
        Func<SceneDocument, TestWorkspace, SceneDocument> extend)
    {
        var document = extend(TestScenes.Instance(workspace), workspace);
        var session = WorkspaceSession.Load(workspace.RootPath);
        var scene = new LoadedScene(
            Path.Combine(session.Workspace.ScenesDirectoryPath, "base.scene.json"),
            document);
        using var parsed = JsonDocument.Parse(File.ReadAllText(SceneExport.Write(session, scene).Path));
        return parsed.RootElement.Clone();
    }
}
