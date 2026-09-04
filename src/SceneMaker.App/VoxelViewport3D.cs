using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SceneMaker.Core;
using SceneMaker.Editor;

namespace SceneMaker.App;

public readonly record struct VoxelPick(
    VoxelCoordinate Cell,
    VoxelCoordinate FaceNormal,
    VoxelPointMeters Position,
    bool HitVoxel);

/// <summary>
/// Godot rendering and input projection for the voxel authoring world. All
/// edit decisions are emitted as picks and remain in VoxelToolInteraction.
/// </summary>
public sealed partial class VoxelViewport3D : SubViewportContainer
{
    private readonly SubViewport _viewport = new();
    private readonly Node3D _world = new();
    private readonly Node3D _voxelRoot = new();
    private readonly Camera3D _camera = new();
    private readonly VoxelCrossSectionView _crossSection = new();

    private VoxelGrid? _grid;
    private IReadOnlyDictionary<string, Color> _colors = new Dictionary<string, Color>();
    private bool _orbiting;
    private bool _painting;
    private float _yaw = -0.75f;
    private float _pitch = -0.65f;
    private float _distance = 35f;
    private Vector3 _target;
    private int? _renderedCutY;
    private VoxelViewportMode _renderedMode = VoxelViewportMode.TopDown;

    public event Action<VoxelPick>? PickPressed;
    public event Action<VoxelPick>? PickDragged;
    public event Action<VoxelPick>? PickHovered;
    public event Action? StrokeEnded;

    public VoxelViewport3D()
    {
        Stretch = true;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    public override void _Ready()
    {
        _viewport.Name = "VoxelViewport";
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _viewport.Msaa3D = Viewport.Msaa.Msaa4X;
        AddChild(_viewport);
        _viewport.AddChild(_world);
        _world.AddChild(_voxelRoot);

        _camera.Name = "AuthoringCamera";
        _camera.Current = true;
        _camera.Fov = 55f;
        _world.AddChild(_camera);

        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55f, -35f, 0f),
            ShadowEnabled = true,
            LightEnergy = 1.15f,
        };
        _world.AddChild(sun);
        _world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Color.FromHtml("#111925"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Color.FromHtml("#B8C7D9"),
                AmbientLightEnergy = 0.55f,
            },
        });

        UpdateCamera();
    }

    public void ShowGrid(
        VoxelGrid grid,
        IEnumerable<WorkspaceAssetProfile> assets,
        bool resetCamera = false)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _colors = assets
            .Where(static profile => profile.Role == WorkspaceAssetRole.Terrain)
            .ToDictionary(
                static profile => profile.AssetKey,
                static profile => Color.FromHtml(profile.Color),
                StringComparer.Ordinal);
        if (resetCamera)
        {
            _crossSection.Follow(grid, null);
            _target = new Vector3(
                grid.WidthVoxels * (float)grid.VoxelSizeMeters / 2f,
                0f,
                grid.DepthVoxels * (float)grid.VoxelSizeMeters / 2f);
            _distance = Math.Clamp(Math.Max(grid.WidthVoxels, grid.DepthVoxels) * 1.35f, 8f, 300f);
            UpdateCamera();
        }
        RebuildVoxels(force: true);
    }

    public void FollowCrossSection(VoxelPointMeters? point)
    {
        if (_grid is null) return;
        var slice = _crossSection.Follow(_grid, point);
        if (slice.Mode == _renderedMode && slice.CutVoxelY == _renderedCutY) return;
        RebuildVoxels(force: true);
    }

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Math.Max(2f, _distance * 0.88f);
                UpdateCamera();
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Math.Min(600f, _distance / 0.88f);
                UpdateCamera();
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                _orbiting = right.Pressed;
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                _painting = left.Pressed;
                if (left.Pressed && TryPick(left.Position, out var pressed))
                    PickPressed?.Invoke(pressed);
                if (!left.Pressed) StrokeEnded?.Invoke();
                GrabFocus();
                AcceptEvent();
                return;
            case InputEventMouseMotion motion when _orbiting:
                _yaw -= motion.Relative.X * 0.008f;
                _pitch = Math.Clamp(_pitch - motion.Relative.Y * 0.008f, -1.48f, -0.08f);
                UpdateCamera();
                AcceptEvent();
                return;
            case InputEventMouseMotion motion:
                if (!TryPick(motion.Position, out var hovered)) return;
                PickHovered?.Invoke(hovered);
                if (_painting) PickDragged?.Invoke(hovered);
                return;
        }
    }

    private void UpdateCamera()
    {
        var planar = MathF.Cos(_pitch) * _distance;
        _camera.Position = _target + new Vector3(
            MathF.Sin(_yaw) * planar,
            -MathF.Sin(_pitch) * _distance,
            MathF.Cos(_yaw) * planar);
        _camera.LookAt(_target, Vector3.Up);
    }

    private void RebuildVoxels(bool force)
    {
        if (_grid is null || !force) return;
        foreach (var child in _voxelRoot.GetChildren()) child.QueueFree();

        var slice = _crossSection.Current;
        _renderedMode = slice.Mode;
        _renderedCutY = slice.CutVoxelY;
        var visible = slice.Mode == VoxelViewportMode.CrossSection
            ? _grid.Cells.Values.Where(cell => cell.Coordinate.Y <= slice.CutVoxelY).ToArray()
            : _grid.Cells.Values.ToArray();

        foreach (var materialGroup in visible.GroupBy(static cell => cell.AssetKey))
        {
            var box = new BoxMesh
            {
                Size = Vector3.One * (float)_grid.VoxelSizeMeters,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = _colors.GetValueOrDefault(
                        materialGroup.Key,
                        Color.FromHtml("#A0A8B2")),
                    Roughness = 0.92f,
                },
            };
            var cells = materialGroup.ToArray();
            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = box,
                InstanceCount = cells.Length,
            };
            for (var index = 0; index < cells.Length; index++)
            {
                var coordinate = cells[index].Coordinate;
                var origin = new Vector3(
                    (coordinate.X + 0.5f) * (float)_grid.VoxelSizeMeters,
                    (coordinate.Y + 0.5f) * (float)_grid.VoxelSizeMeters,
                    (coordinate.Z + 0.5f) * (float)_grid.VoxelSizeMeters);
                multiMesh.SetInstanceTransform(index, new Transform3D(Basis.Identity, origin));
            }
            _voxelRoot.AddChild(new MultiMeshInstance3D { Multimesh = multiMesh });
        }

        AddGroundGrid();

        if (slice.Mode == VoxelViewportMode.CrossSection)
            AddOutline(slice);
    }

    private void AddGroundGrid()
    {
        if (_grid is null) return;
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.45f, 0.62f, 0.75f, 0.42f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        var scale = (float)_grid.VoxelSizeMeters;
        for (var x = 0; x <= _grid.WidthVoxels; x++)
        {
            mesh.SurfaceAddVertex(new Vector3(x * scale, 0.005f, 0f));
            mesh.SurfaceAddVertex(new Vector3(x * scale, 0.005f, _grid.DepthVoxels * scale));
        }
        for (var z = 0; z <= _grid.DepthVoxels; z++)
        {
            mesh.SurfaceAddVertex(new Vector3(0f, 0.005f, z * scale));
            mesh.SurfaceAddVertex(new Vector3(_grid.WidthVoxels * scale, 0.005f, z * scale));
        }
        mesh.SurfaceEnd();
        _voxelRoot.AddChild(new MeshInstance3D { Mesh = mesh });
    }

    private void AddOutline(VoxelViewportSlice slice)
    {
        if (_grid is null || slice.OverheadOutline.Count == 0) return;
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = Color.FromHtml("#FFD866"),
        };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        var y = (float)slice.ElevationMeters + 0.015f;
        var scale = (float)_grid.VoxelSizeMeters;
        foreach (var edge in slice.OverheadOutline)
        {
            mesh.SurfaceAddVertex(new Vector3(edge.StartX * scale, y, edge.StartZ * scale));
            mesh.SurfaceAddVertex(new Vector3(edge.EndX * scale, y, edge.EndZ * scale));
        }
        mesh.SurfaceEnd();
        _voxelRoot.AddChild(new MeshInstance3D { Mesh = mesh });
    }

    private bool TryPick(Vector2 screenPosition, out VoxelPick pick)
    {
        pick = default;
        if (_grid is null) return false;
        var origin = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition).Normalized();
        var bestDistance = float.PositiveInfinity;
        var bestCell = default(VoxelCoordinate);
        var bestNormal = default(VoxelCoordinate);
        var hitVoxel = false;
        foreach (var cell in _grid.Cells.Keys)
        {
            var minimum = new Vector3(cell.X, cell.Y, cell.Z) * (float)_grid.VoxelSizeMeters;
            if (!RayBox(origin, direction, minimum, (float)_grid.VoxelSizeMeters, out var distance, out var normal)
                || distance >= bestDistance)
            {
                continue;
            }
            bestDistance = distance;
            bestCell = cell;
            bestNormal = new VoxelCoordinate((int)normal.X, (int)normal.Y, (int)normal.Z);
            hitVoxel = true;
        }

        Vector3 position;
        if (hitVoxel)
        {
            position = origin + direction * bestDistance;
        }
        else
        {
            if (Math.Abs(direction.Y) < 0.00001f) return false;
            var distance = -origin.Y / direction.Y;
            if (distance < 0f) return false;
            position = origin + direction * distance;
            var x = _grid.VoxelIndex((decimal)position.X);
            var z = _grid.VoxelIndex((decimal)position.Z);
            if (x < 0 || x >= _grid.WidthVoxels || z < 0 || z >= _grid.DepthVoxels)
                return false;
            bestCell = new VoxelCoordinate(x, 0, z);
            bestNormal = new VoxelCoordinate(0, 1, 0);
        }

        pick = new VoxelPick(
            bestCell,
            bestNormal,
            new VoxelPointMeters((decimal)position.X, (decimal)position.Y, (decimal)position.Z),
            hitVoxel);
        return true;
    }

    private static bool RayBox(
        Vector3 origin,
        Vector3 direction,
        Vector3 minimum,
        float size,
        out float distance,
        out Vector3 normal)
    {
        var maximum = minimum + Vector3.One * size;
        var near = float.NegativeInfinity;
        var far = float.PositiveInfinity;
        normal = Vector3.Zero;
        for (var axis = 0; axis < 3; axis++)
        {
            var rayOrigin = origin[axis];
            var rayDirection = direction[axis];
            if (Math.Abs(rayDirection) < 0.000001f)
            {
                if (rayOrigin < minimum[axis] || rayOrigin > maximum[axis])
                {
                    distance = 0f;
                    return false;
                }
                continue;
            }

            var first = (minimum[axis] - rayOrigin) / rayDirection;
            var second = (maximum[axis] - rayOrigin) / rayDirection;
            var entrySign = -Math.Sign(rayDirection);
            if (first > second) (first, second) = (second, first);
            if (first > near)
            {
                near = first;
                normal = axis switch
                {
                    0 => new Vector3(entrySign, 0f, 0f),
                    1 => new Vector3(0f, entrySign, 0f),
                    _ => new Vector3(0f, 0f, entrySign),
                };
            }
            far = Math.Min(far, second);
            if (near > far)
            {
                distance = 0f;
                return false;
            }
        }
        distance = near >= 0f ? near : far;
        return distance >= 0f;
    }
}
