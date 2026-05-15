using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    /// <summary>
    /// Helper for splitting a block's shape into a static body (with managed elements
    /// removed) plus per-element uploaded meshes, so <see cref="KineticAnimatorRenderer"/>
    /// and <see cref="KineticPistonRenderer"/> can transform individual elements per frame
    /// while the rest of the block renders through the standard tesselator pipeline.
    /// </summary>
    public static class KineticMeshSplitter
    {
        /// <summary>
        /// Walks the BE's behaviors and returns the union of element names managed by
        /// <see cref="BEBehaviorKineticAnimator"/> and <see cref="BEBehaviorKineticPiston"/>.
        /// Use the result as the exclusion set when tesselating the static body.
        /// </summary>
        public static string[] CollectManagedElements(BlockEntity be)
        {
            var set = new HashSet<string>();
            var anim = be.GetBehavior<BEBehaviorKineticAnimator>();
            if (anim != null)
                foreach (var name in anim.ManagedElementNames) set.Add(name);
            var piston = be.GetBehavior<BEBehaviorKineticPiston>();
            if (piston != null)
                foreach (var name in piston.ManagedElementNames) set.Add(name);
            var stretch = be.GetBehavior<BEBehaviorKineticStretch>();
            if (stretch != null)
                foreach (var name in stretch.ManagedElementNames) set.Add(name);
            string[] result = new string[set.Count];
            set.CopyTo(result);
            return result;
        }

        /// <summary>Deep-clones <paramref name="orig"/> and removes the named elements from the clone.</summary>
        public static Shape CloneAndRemoveElements(Shape orig, string[] elementNames)
        {
            Shape clone = orig.Clone();
            clone.RemoveElements(elementNames);
            return clone;
        }

        /// <summary>
        /// Returns each requested element name plus every descendant name below matching
        /// elements. Missing names are preserved so callers can still report/select them.
        /// </summary>
        public static string[] ExpandElementTreeNames(Shape shape, IEnumerable<string> elementNames)
        {
            var ordered = new List<string>();
            var seen = new HashSet<string>();

            if (elementNames == null) return ordered.ToArray();

            foreach (string name in elementNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                AddName(name, ordered, seen);

                ShapeElement elem = shape?.GetElementByName(name);
                if (elem?.Children == null) continue;

                foreach (ShapeElement child in elem.Children)
                {
                    AddDescendantNames(child, ordered, seen);
                }
            }

            return ordered.ToArray();
        }

        private static void AddDescendantNames(ShapeElement elem, List<string> ordered, HashSet<string> seen)
        {
            if (elem == null) return;
            AddName(elem.Name, ordered, seen);

            if (elem.Children == null) return;
            foreach (ShapeElement child in elem.Children)
            {
                AddDescendantNames(child, ordered, seen);
            }
        }

        private static void AddName(string name, List<string> ordered, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(name) || !seen.Add(name)) return;
            ordered.Add(name);
        }

        /// <summary>
        /// Returns the named element's <c>RotationOrigin</c> in canonical block units (0..1),
        /// without applying any block-level rotation. The renderer wraps the per-element
        /// transform with the block's full rotation matrix at draw time, so this stays in the
        /// shape file's natural frame. Falls back to the block center if the element is missing.
        /// </summary>
        public static Vec3f GetCanonicalPivotInBlockUnits(Shape shape, string elementName)
        {
            var elem = shape?.GetElementByName(elementName);
            if (elem == null || elem.RotationOrigin == null || elem.RotationOrigin.Length < 3)
                return new Vec3f(0.5f, 0.5f, 0.5f);

            return new Vec3f(
                (float)elem.RotationOrigin[0] / 16f,
                (float)elem.RotationOrigin[1] / 16f,
                (float)elem.RotationOrigin[2] / 16f);
        }

        /// <summary>Block-level shape rotation in degrees, or zero if the block has no shape.</summary>
        public static Vec3f GetBlockShapeRotationDeg(Block block)
        {
            var s = block?.Shape;
            if (s == null) return new Vec3f();
            return new Vec3f(s.rotateX, s.rotateY, s.rotateZ);
        }

        /// <summary>
        /// Loads, clones, and tesselates the block's shape with the named elements removed.
        /// Bakes the full <c>rotateX/Y/Z</c> from the block's shape into the body mesh so a
        /// single shape file can drive every axis variant via <c>shapeByType</c>. Result is
        /// the static body mesh ready to push into a mesh pool from <c>OnTesselation</c>.
        /// Returns null if the block has no shape.
        /// </summary>
        public static MeshData TesselateBodyExcluding(ICoreClientAPI capi, Block block, ITesselatorAPI tess, string[] excludedElements)
        {
            Shape orig = LoadShape(capi, block);
            if (orig == null) return null;
            // Shape.RemoveElements walks the tree, so removing a parent already drops every
            // descendant — no name expansion needed here.
            Shape stripped = CloneAndRemoveElements(orig, excludedElements);
            tess.TesselateShape(block, stripped, out MeshData mesh, GetBlockShapeRotationDeg(block));
            return mesh;
        }

        /// <summary>
        /// Tesselates a single named element of the block's shape and uploads it as a
        /// <see cref="MeshRef"/>. The element mesh is left in canonical (unrotated) frame —
        /// renderers must apply the block's shape rotation as part of their model matrix to
        /// keep elements aligned with the body. Returns (null, block-center) if missing.
        /// </summary>
        public static (MeshRef mesh, Vec3f pivot) TesselateElement(ICoreClientAPI capi, Block block, string elementName)
        {
            Shape orig = LoadShape(capi, block);
            if (orig == null) return (null, new Vec3f(0.5f, 0.5f, 0.5f));

            Vec3f pivot = GetCanonicalPivotInBlockUnits(orig, elementName);
            // VS's selectiveElements matcher treats a bare name as "this element with NO
            // children": the matched element renders, but its child haystack is set to
            // empty so descendants are silently dropped. The "name/*" wildcard form is
            // what tells the matcher to recurse — so a single rotator entry covers the
            // parent plus every descendant baked at their parent-relative transforms.
            string[] selectiveElements = new[] { elementName + "/*" };

            capi.Tesselator.TesselateShape(block, orig, out MeshData mesh,
                new Vec3f(), null, selectiveElements);
            if (mesh == null) return (null, pivot);
            return (capi.Render.UploadMesh(mesh), pivot);
        }

        private static Shape LoadShape(ICoreClientAPI capi, Block block)
        {
            if (block?.Shape?.Base == null) return null;
            AssetLocation loc = block.Shape.Base.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");
            return Shape.TryGet(capi, loc);
        }
    }
}
