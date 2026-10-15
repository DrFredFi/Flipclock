using System.Numerics;
using System.Runtime.InteropServices;

namespace Flipclock.Rendering;

/// <summary>Which surface of a mesh a vertex belongs to; the pixel shader picks the glyph by it.</summary>
internal static class FaceKind
{
    public const float Front = 0;
    public const float Back = 1;
    public const float Edge = 2;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct Vertex(Vector3 Position, Vector3 Normal, Vector2 Uv, float Face)
{
    public const int Stride = 36;
}

internal sealed class MeshData
{
    public List<Vertex> Vertices { get; } = [];
    public List<uint> Indices { get; } = [];

    uint Add(Vertex v)
    {
        Vertices.Add(v);
        return (uint)(Vertices.Count - 1);
    }

    void Quad(uint a, uint b, uint c, uint d)
    {
        Indices.AddRange([a, b, c, a, c, d]);
    }

    /// <summary>
    /// One half of a split-flap tile in unit space: tile width 1, centered on x = 0, the hinge at y = 0.
    /// The top half spans y ∈ [gap/2, height/2]; the bottom half the mirror image. The outer corners are
    /// rounded with <paramref name="cornerRadius"/>, all edges are bevelled, and the slab runs from
    /// <paramref name="zBack"/> to <paramref name="zFront"/>.
    /// Front-face UVs map to the full-tile glyph; back-face UVs map to the glyph's opposite half as seen
    /// after the flap has rotated 180° around the hinge.
    /// </summary>
    public static MeshData HalfTile(float height, float gap, float zBack, float zFront, float cornerRadius, float bevel, bool top, int cornerSegments = 10)
    {
        var mesh = new MeshData();
        bevel = MathF.Min(bevel, (zFront - zBack) * 0.45f);
        float hingeRadius = bevel * 1.05f;
        float x0 = -0.5f, x1 = 0.5f;
        float y0 = top ? gap / 2 : -height / 2;
        float y1 = top ? height / 2 : -gap / 2;
        float rOuter = MathF.Max(cornerRadius, hingeRadius);

        // Corners counter-clockwise: bottom-right, top-right, top-left, bottom-left.
        (float cx, float cy, float r, float a0)[] corners =
        [
            (x1, y0, top ? hingeRadius : rOuter, -90),
            (x1, y1, top ? rOuter : hingeRadius, 0),
            (x0, y1, top ? rOuter : hingeRadius, 90),
            (x0, y0, top ? hingeRadius : rOuter, 180),
        ];

        var dirs = new List<Vector2>();
        var centers = new List<Vector2>();
        var radii = new List<float>();
        foreach (var (cx, cy, r, a0) in corners)
        {
            float centerX = cx < 0 ? cx + r : cx - r;
            // y0 is always the lower edge, y1 the upper edge.
            float centerY = cy == y0 ? cy + r : cy - r;
            for (int s = 0; s <= cornerSegments; s++)
            {
                float a = (a0 + 90f * s / cornerSegments) * MathF.PI / 180f;
                dirs.Add(new Vector2(MathF.Cos(a), MathF.Sin(a)));
                centers.Add(new Vector2(centerX, centerY));
                radii.Add(r);
            }
        }
        int n = dirs.Count;

        Vector2 Ring(int i, float inset) => centers[i] + dirs[i] * (radii[i] - inset);
        Vector2 FrontUv(Vector2 p) => new(0.5f + p.X, 0.5f - p.Y / height);
        Vector2 BackUv(Vector2 p) => new(0.5f + p.X, 0.5f + p.Y / height);

        // Front face (fan around the centroid).
        var centroid = new Vector2(0, (y0 + y1) / 2);
        uint fc = mesh.Add(new Vertex(new Vector3(centroid, zFront), Vector3.UnitZ, FrontUv(centroid), FaceKind.Front));
        uint frontStart = (uint)mesh.Vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var p = Ring(i, bevel);
            mesh.Add(new Vertex(new Vector3(p, zFront), Vector3.UnitZ, FrontUv(p), FaceKind.Front));
        }
        for (int i = 0; i < n; i++)
            mesh.Indices.AddRange([fc, frontStart + (uint)i, frontStart + (uint)((i + 1) % n)]);

        // Bevel: a rounded chamfer from the front face out to the side wall.
        const int bevelSteps = 3;
        uint[] previous = new uint[n];
        for (int step = 0; step <= bevelSteps; step++)
        {
            float t = (float)step / bevelSteps;
            float angle = t * MathF.PI / 2;
            float inset = bevel * (1 - MathF.Sin(angle));
            float z = zFront - bevel * (1 - MathF.Cos(angle));
            var current = new uint[n];
            for (int i = 0; i < n; i++)
            {
                var p = Ring(i, inset);
                var normal = Vector3.Normalize(new Vector3(dirs[i] * MathF.Sin(angle), MathF.Cos(angle)));
                current[i] = mesh.Add(new Vertex(new Vector3(p, z), normal, FrontUv(p), FaceKind.Edge));
            }
            if (step > 0)
                for (int i = 0; i < n; i++)
                    mesh.Quad(previous[i], previous[(i + 1) % n], current[(i + 1) % n], current[i]);
            previous = current;
        }

        // Side wall.
        float sideTop = zFront - bevel;
        var wallTop = new uint[n];
        var wallBottom = new uint[n];
        for (int i = 0; i < n; i++)
        {
            var p = Ring(i, 0);
            var normal = new Vector3(dirs[i], 0);
            wallTop[i] = mesh.Add(new Vertex(new Vector3(p, sideTop), normal, Vector2.Zero, FaceKind.Edge));
            wallBottom[i] = mesh.Add(new Vertex(new Vector3(p, zBack), normal, Vector2.Zero, FaceKind.Edge));
        }
        for (int i = 0; i < n; i++)
            mesh.Quad(wallTop[i], wallTop[(i + 1) % n], wallBottom[(i + 1) % n], wallBottom[i]);

        // Back face.
        uint bc = mesh.Add(new Vertex(new Vector3(centroid, zBack), -Vector3.UnitZ, BackUv(centroid), FaceKind.Back));
        uint backStart = (uint)mesh.Vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var p = Ring(i, 0);
            mesh.Add(new Vertex(new Vector3(p, zBack), -Vector3.UnitZ, BackUv(p), FaceKind.Back));
        }
        for (int i = 0; i < n; i++)
            mesh.Indices.AddRange([bc, backStart + (uint)((i + 1) % n), backStart + (uint)i]);

        return mesh;
    }

    /// <summary>Unit sphere centered at the origin.</summary>
    public static MeshData Sphere(int rings = 18, int segments = 28)
    {
        var mesh = new MeshData();
        for (int r = 0; r <= rings; r++)
        {
            float phi = MathF.PI * r / rings;
            for (int s = 0; s <= segments; s++)
            {
                float theta = 2 * MathF.PI * s / segments;
                var p = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
                mesh.Add(new Vertex(p, p, Vector2.Zero, FaceKind.Edge));
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                uint a = (uint)(r * (segments + 1) + s), b = a + 1, c = a + (uint)segments + 2, d = a + (uint)segments + 1;
                mesh.Quad(a, b, c, d);
            }
        return mesh;
    }

    /// <summary>Unit cylinder along the x axis: radius 1, x ∈ [-0.5, 0.5], capped.</summary>
    public static MeshData CylinderX(int segments = 20)
    {
        var mesh = new MeshData();
        for (int s = 0; s <= segments; s++)
        {
            float a = 2 * MathF.PI * s / segments;
            var n = new Vector3(0, MathF.Cos(a), MathF.Sin(a));
            mesh.Add(new Vertex(new Vector3(-0.5f, n.Y, n.Z), n, Vector2.Zero, FaceKind.Edge));
            mesh.Add(new Vertex(new Vector3(0.5f, n.Y, n.Z), n, Vector2.Zero, FaceKind.Edge));
        }
        for (int s = 0; s < segments; s++)
        {
            uint a = (uint)(s * 2);
            mesh.Quad(a, a + 1, a + 3, a + 2);
        }
        foreach (float x in new[] { -0.5f, 0.5f })
        {
            var normal = new Vector3(MathF.Sign(x), 0, 0);
            uint center = mesh.Add(new Vertex(new Vector3(x, 0, 0), normal, Vector2.Zero, FaceKind.Edge));
            uint start = (uint)mesh.Vertices.Count;
            for (int s = 0; s < segments; s++)
            {
                float a = 2 * MathF.PI * s / segments;
                mesh.Add(new Vertex(new Vector3(x, MathF.Cos(a), MathF.Sin(a)), normal, Vector2.Zero, FaceKind.Edge));
            }
            for (int s = 0; s < segments; s++)
                mesh.Indices.AddRange([center, start + (uint)s, start + (uint)((s + 1) % segments)]);
        }
        return mesh;
    }

    /// <summary>Unit quad in the xy plane facing +z, UV (0,0) at the top-left.</summary>
    public static MeshData Quad()
    {
        var mesh = new MeshData();
        mesh.Add(new Vertex(new Vector3(-0.5f, 0.5f, 0), Vector3.UnitZ, new Vector2(0, 0), FaceKind.Edge));
        mesh.Add(new Vertex(new Vector3(0.5f, 0.5f, 0), Vector3.UnitZ, new Vector2(1, 0), FaceKind.Edge));
        mesh.Add(new Vertex(new Vector3(0.5f, -0.5f, 0), Vector3.UnitZ, new Vector2(1, 1), FaceKind.Edge));
        mesh.Add(new Vertex(new Vector3(-0.5f, -0.5f, 0), Vector3.UnitZ, new Vector2(0, 1), FaceKind.Edge));
        mesh.Quad(0, 1, 2, 3);
        return mesh;
    }
}
