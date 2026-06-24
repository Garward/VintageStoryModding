using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
        private static readonly float[] BlankStaticSideUv = new[] { 0f, 0f, 0.25f, 0.25f };

        private void DisposeAnimationRenderer()
        {
            if (Api is ICoreClientAPI capi && animationRenderer != null)
            {
                capi.Event.UnregisterRenderer(animationRenderer, EnumRenderStage.Opaque);
                animationRenderer.Dispose();
                animationRenderer = null;
            }
        }

        /// <summary>
        /// Pick a shape per chain part and shaft state, rotated to match the belt's direction.
        /// Solo and End use the end shape with travel-direction-facing pulley wrap; Start uses the
        /// same shape rotated 180° so the wrap appears at the back. Middle uses the open
        /// top-and-bottom shape, or the shaft-through variant when <see cref="HasShaft"/> is set.
        /// </summary>
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (Direction == null) return false;
            ICoreClientAPI capi = Api as ICoreClientAPI;
            if (capi == null) return false;

            string shapePath = Part switch
            {
                EnumBeltPart.Middle when HasShaft => "vintagekinematics:shapes/block/belt-shaft.json",
                EnumBeltPart.Middle              => "vintagekinematics:shapes/block/belt-middle.json",
                _                                => "vintagekinematics:shapes/block/belt-end.json"
            };

            Shape shape = Shape.TryGet(capi, new AssetLocation(shapePath));
            if (shape == null) return false;
            shape = shape.Clone();
            if (Part != EnumBeltPart.Middle || HasShaft)
            {
                shape.RemoveElements(new[] { "Shaft", "FlatWrap" });
            }
            UseBlankBeltBandForStaticSideFaces(shape);
            DisableInternalBeltStripCaps(shape);
            EnableStaticBeltRunFallback(shape);

            int baseRot = Direction switch
            {
                "n" => 0,
                "e" => 270,
                "s" => 180,
                "w" => 90,
                _   => 0
            };
            // Shape is authored with the belt strips extending from the shaft toward +Z, i.e.
            // toward the chain side when this is an End block at the head. Start (at the tail)
            // needs a 180° flip so its strips face -Z toward the chain instead.
            if (Part == EnumBeltPart.Start)
            {
                baseRot = (baseRot + 180) % 360;
            }

            tessThreadTesselator.TesselateShape(Block, shape, out MeshData mesh, new Vec3f(0, baseRot, 0));
            if (mesh != null) mesher.AddMeshData(mesh);
            return true;
        }

        private static void UseBlankBeltBandForStaticSideFaces(Shape shape)
        {
            if (shape == null) return;

            SetElementFacesUv(shape, "TopBelt", BlankStaticSideUv,
                BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST);
            SetElementFacesUv(shape, "BottomBelt", BlankStaticSideUv,
                BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST);
            SetElementFacesUv(shape, "FlatWrapInside", BlankStaticSideUv,
                BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST);
            SetElementFacesUv(shape, "FlatWrapWall1", BlankStaticSideUv,
                BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST);
            SetElementFacesUv(shape, "FlatWrapWall2", BlankStaticSideUv,
                BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST);
        }

        private void DisableInternalBeltStripCaps(Shape shape)
        {
            if (shape == null) return;

            if (Part == EnumBeltPart.Middle)
            {
                DisableElementFaces(shape, "TopBelt", BlockFacing.NORTH, BlockFacing.SOUTH);
                DisableElementFaces(shape, "BottomBelt", BlockFacing.NORTH, BlockFacing.SOUTH);
                return;
            }

            if (Part == EnumBeltPart.Start || Part == EnumBeltPart.End)
            {
                DisableElementFaces(shape, "TopBelt", BlockFacing.SOUTH);
                DisableElementFaces(shape, "BottomBelt", BlockFacing.SOUTH);
            }
        }

        private static void DisableElementFaces(Shape shape, string elementName, params BlockFacing[] faces)
        {
            ShapeElement elem = shape.GetElementByName(elementName);
            if (elem?.FacesResolved == null) return;

            for (int i = 0; i < faces.Length; i++)
            {
                ShapeElementFace face = elem.FacesResolved[faces[i].Index];
                if (face != null) face.Enabled = false;
            }
        }

        private static void EnableStaticBeltRunFallback(Shape shape)
        {
            EnableElementFaces(shape, "TopBelt", BlockFacing.UP);
            EnableElementFaces(shape, "BottomBelt", BlockFacing.DOWN);
        }

        private static void EnableElementFaces(Shape shape, string elementName, params BlockFacing[] faces)
        {
            ShapeElement elem = shape.GetElementByName(elementName);
            if (elem?.FacesResolved == null) return;

            for (int i = 0; i < faces.Length; i++)
            {
                ShapeElementFace face = elem.FacesResolved[faces[i].Index];
                if (face != null) face.Enabled = true;
            }
        }

        private static void SetElementFacesUv(Shape shape, string elementName, float[] uv, params BlockFacing[] faces)
        {
            ShapeElement elem = shape.GetElementByName(elementName);
            if (elem?.FacesResolved == null) return;

            for (int i = 0; i < faces.Length; i++)
            {
                ShapeElementFace face = elem.FacesResolved[faces[i].Index];
                if (face != null) face.Uv = (float[])uv.Clone();
            }
        }
    }
}
