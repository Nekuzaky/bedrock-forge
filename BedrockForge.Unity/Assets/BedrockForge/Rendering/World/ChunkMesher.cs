using System.Collections.Generic;
using BedrockForge.Rendering.Blocks;
using UnityEngine;

namespace BedrockForge.Rendering.World
{
    /// <summary>Vertex data for one mesh, built off the main thread and uploaded later.</summary>
    public sealed class MeshBuffers
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Color> Colors = new List<Color>();
        /// <summary>u, v and texture-array slice.</summary>
        public readonly List<Vector3> Uvs = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();

        public bool IsEmpty => Vertices.Count == 0;

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetColors(Colors);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0, true);
            return mesh;
        }
    }

    public sealed class ChunkMeshData
    {
        public int ChunkX;
        public int ChunkZ;
        public readonly MeshBuffers Opaque = new MeshBuffers();
        public readonly MeshBuffers Cutout = new MeshBuffers();
        public readonly MeshBuffers Transparent = new MeshBuffers();
        public int BlockCount;

        public MeshBuffers For(RenderLayer layer) =>
            layer == RenderLayer.Opaque ? Opaque : layer == RenderLayer.Cutout ? Cutout : Transparent;
    }

    /// <summary>
    /// Builds the meshes of one chunk column. Every block shape is a few axis-aligned boxes (a slab is one box,
    /// stairs two, a fence a post plus bars); box faces lying on the block boundary are culled against full
    /// neighbours. Full cubes also get per-vertex ambient occlusion. Coordinates are Minecraft's (Y up); the
    /// viewer mirrors Z so the world is not flipped in Unity.
    /// </summary>
    public static class ChunkMesher
    {
        // Face order: +Y, -Y, +X, -X, +Z, -Z. Corners are clockwise seen from outside (Unity front faces).
        private static readonly Vector3Int[] Normals =
        {
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0), new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        private static readonly Vector3Int[][] Corners =
        {
            new[] { V(0, 1, 0), V(0, 1, 1), V(1, 1, 1), V(1, 1, 0) },
            new[] { V(0, 0, 0), V(1, 0, 0), V(1, 0, 1), V(0, 0, 1) },
            new[] { V(1, 0, 0), V(1, 1, 0), V(1, 1, 1), V(1, 0, 1) },
            new[] { V(0, 0, 1), V(0, 1, 1), V(0, 1, 0), V(0, 0, 0) },
            new[] { V(1, 0, 1), V(1, 1, 1), V(0, 1, 1), V(0, 0, 1) },
            new[] { V(0, 0, 0), V(0, 1, 0), V(1, 1, 0), V(1, 0, 0) },
        };

        /// <summary>Directional light baked per face, close to the vanilla look.</summary>
        private static readonly float[] FaceShade = { 1f, 0.5f, 0.8f, 0.8f, 0.65f, 0.65f };

        private static readonly float[] AoCurve = { 0.45f, 0.65f, 0.82f, 1f };

        /// <summary>For each face and corner: the two side neighbours and the diagonal one, in front of the face.</summary>
        private static readonly Vector3Int[,,] AoOffsets = BuildAoOffsets();

        /// <summary>Neighbour direction for the four horizontal sides, in <see cref="BlockVisual.WallSides"/> order.</summary>
        private static readonly Vector3Int[] Sides = { new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

        private const float LiquidTop = 14f / 16f;

        private static readonly Box FullBox = new Box(Vector3.zero, Vector3.one);

        private readonly struct Box
        {
            public readonly Vector3 Min;
            public readonly Vector3 Max;

            public Box(Vector3 min, Vector3 max)
            {
                Min = min;
                Max = max;
            }

            /// <summary>Box from pixel coordinates (0–16).</summary>
            public static Box Px(float x0, float y0, float z0, float x1, float y1, float z1) =>
                new Box(new Vector3(x0, y0, z0) / 16f, new Vector3(x1, y1, z1) / 16f);

            public bool IsFull => Min == Vector3.zero && Max == Vector3.one;

            /// <summary>True when the face lies on the block's outer boundary, where a neighbour can hide it.</summary>
            public bool OnBoundary(int face) => face switch
            {
                0 => Max.y >= 1f,
                1 => Min.y <= 0f,
                2 => Max.x >= 1f,
                3 => Min.x <= 0f,
                4 => Max.z >= 1f,
                _ => Min.z <= 0f,
            };
        }

        private static Vector3Int V(int x, int y, int z) => new Vector3Int(x, y, z);

        private static Vector3Int[,,] BuildAoOffsets()
        {
            var result = new Vector3Int[6, 4, 3];
            for (int f = 0; f < 6; f++)
            {
                var n = Normals[f];
                int axis = n.x != 0 ? 0 : n.y != 0 ? 1 : 2;
                int t1 = (axis + 1) % 3, t2 = (axis + 2) % 3;
                for (int c = 0; c < 4; c++)
                {
                    var corner = Corners[f][c];
                    var d1 = Vector3Int.zero;
                    var d2 = Vector3Int.zero;
                    d1[t1] = corner[t1] == 1 ? 1 : -1;
                    d2[t2] = corner[t2] == 1 ? 1 : -1;
                    result[f, c, 0] = n + d1;
                    result[f, c, 1] = n + d2;
                    result[f, c, 2] = n + d1 + d2;
                }
            }
            return result;
        }

        public static ChunkMeshData Build(WorldData world, ChunkColumn column, BlockVisual[] visuals)
        {
            var data = new ChunkMeshData { ChunkX = column.X, ChunkZ = column.Z };
            var ctx = new Context(world, column, visuals);
            var boxes = new List<Box>(8);

            foreach (var section in column.Sections)
            {
                int baseY = section.Key * 16;
                var ids = section.Value;

                for (int i = 0; i < ids.Length; i++)
                {
                    ushort id = ids[i];
                    if (id == BlockRegistry.AirId)
                        continue;

                    var visual = visuals[id];
                    if (visual.IsAir)
                        continue;

                    int x = i >> 8, z = (i >> 4) & 15, y = baseY + (i & 15);
                    var target = data.For(visual.Layer);
                    data.BlockCount++;

                    if (visual.Shape == BlockShape.Cross)
                    {
                        AddCross(target, x, y, z, visual);
                        continue;
                    }

                    boxes.Clear();
                    BuildBoxes(ctx, x, y, z, visual, boxes);
                    bool ao = visual.CastsAo && visual.Shape == BlockShape.Cube;
                    foreach (var box in boxes)
                        AddBox(ctx, target, x, y, z, id, visual, box, ao && box.IsFull);
                }
            }

            return data;
        }

        private static void BuildBoxes(in Context ctx, int x, int y, int z, BlockVisual v, List<Box> boxes)
        {
            switch (v.Shape)
            {
                case BlockShape.Slab:
                    boxes.Add(v.TopHalf ? Box.Px(0, 8, 0, 16, 16, 16) : Box.Px(0, 0, 0, 16, 8, 16));
                    break;

                case BlockShape.Stairs:
                {
                    float lo = v.UpsideDown ? 8 : 0, hi = v.UpsideDown ? 16 : 8;      // the full-width half
                    float sLo = v.UpsideDown ? 0 : 8, sHi = v.UpsideDown ? 8 : 16;   // the step on the facing side
                    boxes.Add(Box.Px(0, lo, 0, 16, hi, 16));
                    boxes.Add(v.StairFacing switch
                    {
                        0 => Box.Px(8, sLo, 0, 16, sHi, 16),
                        1 => Box.Px(0, sLo, 0, 8, sHi, 16),
                        2 => Box.Px(0, sLo, 8, 16, sHi, 16),
                        _ => Box.Px(0, sLo, 0, 16, sHi, 8),
                    });
                    break;
                }

                case BlockShape.Fence:
                    boxes.Add(Box.Px(6, 0, 6, 10, 16, 10));
                    for (int s = 0; s < 4; s++)
                    {
                        if (!Connects(ctx, x, y, z, v, s))
                            continue;
                        boxes.Add(Arm(s, 7, 9, 12, 15));
                        boxes.Add(Arm(s, 7, 9, 6, 9));
                    }
                    break;

                case BlockShape.Pane:
                {
                    boxes.Add(Box.Px(7, 0, 7, 9, 16, 9));
                    for (int s = 0; s < 4; s++)
                        if (Connects(ctx, x, y, z, v, s))
                            boxes.Add(Arm(s, 7, 9, 0, 16));
                    break;
                }

                case BlockShape.Wall:
                    if (v.WallPost)
                        boxes.Add(Box.Px(4, 0, 4, 12, 16, 12));
                    for (int s = 0; s < 4; s++)
                        if (v.WallSides[s] != 0)
                            boxes.Add(Arm(s, 5, 11, 0, v.WallSides[s] == 2 ? 16 : 14));
                    break;

                case BlockShape.Layer:
                    boxes.Add(new Box(Vector3.zero, new Vector3(1f, v.LayerHeight, 1f)));
                    break;

                case BlockShape.Liquid:
                {
                    var above = ctx.Visuals[ctx.Get(x, y + 1, z)];
                    bool full = above.Shape == BlockShape.Liquid && above.LiquidGroup == v.LiquidGroup;
                    boxes.Add(full ? FullBox : new Box(Vector3.zero, new Vector3(1f, LiquidTop, 1f)));
                    break;
                }

                default:
                    boxes.Add(FullBox);
                    break;
            }
        }

        /// <summary>A bar from the block centre to side <paramref name="side"/>, <paramref name="w0"/>–<paramref name="w1"/> wide.</summary>
        private static Box Arm(int side, float w0, float w1, float y0, float y1) => side switch
        {
            0 => Box.Px(8, y0, w0, 16, y1, w1),
            1 => Box.Px(0, y0, w0, 8, y1, w1),
            2 => Box.Px(w0, y0, 8, w1, y1, 16),
            _ => Box.Px(w0, y0, 0, w1, y1, 8),
        };

        private static bool Connects(in Context ctx, int x, int y, int z, BlockVisual v, int side)
        {
            var d = Sides[side];
            var n = ctx.Visuals[ctx.Get(x + d.x, y, z + d.z)];
            return n.FullOpaque || (n.ConnectGroup != null && n.ConnectGroup == v.ConnectGroup)
                   || (v.Shape == BlockShape.Pane && n.Shape == BlockShape.Wall);
        }

        private static bool FaceVisible(BlockVisual self, ushort selfId, BlockVisual neighbour, ushort neighbourId)
        {
            if (neighbour.FullOpaque)
                return false;
            if (self.Shape == BlockShape.Liquid)
                return !(neighbour.Shape == BlockShape.Liquid && neighbour.LiquidGroup == self.LiquidGroup);
            if (self.SelfCull && neighbourId == selfId)
                return false;
            return true;
        }

        private static void AddBox(in Context ctx, MeshBuffers buf, int x, int y, int z, ushort id, BlockVisual visual, Box box, bool ambientOcclusion)
        {
            for (int f = 0; f < 6; f++)
            {
                if (box.OnBoundary(f))
                {
                    var n = Normals[f];
                    ushort neighbourId = ctx.Get(x + n.x, y + n.y, z + n.z);
                    if (!FaceVisible(visual, id, ctx.Visuals[neighbourId], neighbourId))
                        continue;
                }

                float shade = FaceShade[f];
                var color = visual.FaceColors[f];
                float slice = visual.Slices[f];

                int ao0 = 3, ao1 = 3, ao2 = 3, ao3 = 3;
                if (ambientOcclusion)
                {
                    ao0 = ctx.Ao(x, y, z, f, 0);
                    ao1 = ctx.Ao(x, y, z, f, 1);
                    ao2 = ctx.Ao(x, y, z, f, 2);
                    ao3 = ctx.Ao(x, y, z, f, 3);
                }

                int start = buf.Vertices.Count;
                AddVertex(buf, x, y, z, f, box, Corners[f][0], color, shade * AoCurve[ao0], visual.Alpha, slice);
                AddVertex(buf, x, y, z, f, box, Corners[f][1], color, shade * AoCurve[ao1], visual.Alpha, slice);
                AddVertex(buf, x, y, z, f, box, Corners[f][2], color, shade * AoCurve[ao2], visual.Alpha, slice);
                AddVertex(buf, x, y, z, f, box, Corners[f][3], color, shade * AoCurve[ao3], visual.Alpha, slice);

                // Split the quad along the brighter diagonal so AO gradients do not look creased.
                if (ao0 + ao2 >= ao1 + ao3)
                    AddQuad(buf.Triangles, start, 0, 1, 2, 3);
                else
                    AddQuad(buf.Triangles, start, 1, 2, 3, 0);
            }
        }

        private static void AddVertex(MeshBuffers buf, int x, int y, int z, int face, Box box, Vector3Int corner, Color color, float light, float alpha, float slice)
        {
            var p = new Vector3(
                corner.x == 0 ? box.Min.x : box.Max.x,
                corner.y == 0 ? box.Min.y : box.Max.y,
                corner.z == 0 ? box.Min.z : box.Max.z);

            buf.Vertices.Add(new Vector3(x + p.x, y + p.y, z + p.z));
            buf.Colors.Add(new Color(color.r * light, color.g * light, color.b * light, alpha));
            buf.Uvs.Add(FaceUv(face, p, slice));
        }

        /// <summary>
        /// Texture coordinates from the position inside the block, so partial boxes show the matching part of the
        /// texture. "Right" is chosen as seen from outside each face in Minecraft's right-handed coordinates.
        /// </summary>
        private static Vector3 FaceUv(int face, Vector3 p, float slice) => face switch
        {
            0 => new Vector3(p.x, 1f - p.z, slice),
            1 => new Vector3(p.x, p.z, slice),
            2 => new Vector3(1f - p.z, p.y, slice),
            3 => new Vector3(p.z, p.y, slice),
            4 => new Vector3(p.x, p.y, slice),
            _ => new Vector3(1f - p.x, p.y, slice),
        };

        private static void AddQuad(List<int> tris, int start, int a, int b, int c, int d)
        {
            tris.Add(start + a);
            tris.Add(start + b);
            tris.Add(start + c);
            tris.Add(start + a);
            tris.Add(start + c);
            tris.Add(start + d);
        }

        /// <summary>Two diagonal quads; the cutout shader is double-sided.</summary>
        private static void AddCross(MeshBuffers buf, int x, int y, int z, BlockVisual v)
        {
            const float lo = 0.15f, hi = 0.85f;
            var c = v.FaceColors[0] * 0.9f;
            c.a = 1f;
            float slice = v.Slices[0];
            AddQuadUv(buf, new Vector3(x + lo, y, z + lo), new Vector3(x + lo, y + 1, z + lo), new Vector3(x + hi, y + 1, z + hi), new Vector3(x + hi, y, z + hi), c, slice);
            AddQuadUv(buf, new Vector3(x + lo, y, z + hi), new Vector3(x + lo, y + 1, z + hi), new Vector3(x + hi, y + 1, z + lo), new Vector3(x + hi, y, z + lo), c, slice);
        }

        private static void AddQuadUv(MeshBuffers buf, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float slice)
        {
            int start = buf.Vertices.Count;
            buf.Vertices.Add(a);
            buf.Vertices.Add(b);
            buf.Vertices.Add(c);
            buf.Vertices.Add(d);
            buf.Uvs.Add(new Vector3(0, 0, slice));
            buf.Uvs.Add(new Vector3(0, 1, slice));
            buf.Uvs.Add(new Vector3(1, 1, slice));
            buf.Uvs.Add(new Vector3(1, 0, slice));
            for (int i = 0; i < 4; i++)
                buf.Colors.Add(color);
            AddQuad(buf.Triangles, start, 0, 1, 2, 3);
        }

        /// <summary>Block lookups relative to the chunk being meshed, falling back to neighbour columns at the edges.</summary>
        private readonly struct Context
        {
            private readonly WorldData _world;
            private readonly ChunkColumn _column;
            public readonly BlockVisual[] Visuals;

            public Context(WorldData world, ChunkColumn column, BlockVisual[] visuals)
            {
                _world = world;
                _column = column;
                Visuals = visuals;
            }

            public ushort Get(int lx, int y, int lz)
            {
                ushort id = (uint)lx < 16 && (uint)lz < 16
                    ? _column.Get(lx, y, lz)
                    : _world.GetBlock(_column.X * 16 + lx, y, _column.Z * 16 + lz);
                // A neighbour decoded after our visuals snapshot may use newer ids: treat it as air for now.
                return id < Visuals.Length ? id : BlockRegistry.AirId;
            }

            private bool Occludes(int x, int y, int z, Vector3Int offset) =>
                Visuals[Get(x + offset.x, y + offset.y, z + offset.z)].CastsAo;

            /// <summary>0 (darkest) to 3 (fully lit), the classic voxel AO rule.</summary>
            public int Ao(int x, int y, int z, int face, int corner)
            {
                bool side1 = Occludes(x, y, z, AoOffsets[face, corner, 0]);
                bool side2 = Occludes(x, y, z, AoOffsets[face, corner, 1]);
                if (side1 && side2)
                    return 0;
                bool diagonal = Occludes(x, y, z, AoOffsets[face, corner, 2]);
                return 3 - ((side1 ? 1 : 0) + (side2 ? 1 : 0) + (diagonal ? 1 : 0));
            }
        }
    }
}
