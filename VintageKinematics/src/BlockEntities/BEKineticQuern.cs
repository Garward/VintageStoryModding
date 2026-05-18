using System;
using System.Collections;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEKineticQuern : BlockEntityQuern, IFaceMappedContainer
    {
        private const int SlotInput = 0;
        private const int SlotOutput = 1;
        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;

        // Reflected once: vanilla BEQuern fields are private. The tick handler keeps
        // quantityPlayersGrinding in sync with (real player count + 1 if network active),
        // which drives vanilla's render/sync paths without needing a vanilla MP network.
        private static readonly FieldInfo qpgField =
            typeof(BlockEntityQuern).GetField(
                "quantityPlayersGrinding",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo playersGrindingField =
            typeof(BlockEntityQuern).GetField(
                "playersGrinding",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo rendererField =
            typeof(BlockEntityQuern).GetField(
                "renderer",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private MeshData baseMesh;
        private MeshData topMesh;
        private KineticQuernTopRenderer topRenderer;
        private IOFaceMap ioFaces;
        private string placementFacingCode;

        public IOFaceMap IOFaces => ioFaces;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            BuildIOFaceMap();

            if (api.Side == EnumAppSide.Client)
            {
                ReplaceVanillaRenderer(api as ICoreClientAPI);
            }

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnKineticTick, 250);
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (Api is not ICoreClientAPI capi || Block == null) return false;

            EnsureMeshes(capi, tessThreadTesselator);
            if (baseMesh != null) mesher.AddMeshData(baseMesh);

            return true;
        }

        private void BuildIOFaceMap()
        {
            BlockFacing facing = FaceFromCode(placementFacingCode) ?? BlockFacing.SOUTH;
            BlockFacing inputFace = LeftOf(facing);
            BlockFacing outputFace = RightOf(facing);

            ioFaces = new IOFaceMap(Pos)
                .MapInput(BlockFacing.UP, SlotInput)
                .MapInput(inputFace, SlotInput)
                .MapOutput(outputFace, SlotOutput)
                .MapOutput(BlockFacing.DOWN, SlotOutput);
        }

        public void SetPlacementFacing(BlockFacing facing)
        {
            if (facing == null) return;
            placementFacingCode = facing.Code;
            BuildIOFaceMap();
            MarkDirty(true);
        }

        private static BlockFacing LeftOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.WEST;
            if (facing == BlockFacing.EAST) return BlockFacing.NORTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.EAST;
            if (facing == BlockFacing.WEST) return BlockFacing.SOUTH;
            return BlockFacing.EAST;
        }

        private static BlockFacing RightOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.EAST;
            if (facing == BlockFacing.EAST) return BlockFacing.SOUTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.WEST;
            if (facing == BlockFacing.WEST) return BlockFacing.NORTH;
            return BlockFacing.WEST;
        }

        private static BlockFacing FaceFromCode(string code)
        {
            switch (code)
            {
                case "north":
                case "n": return BlockFacing.NORTH;
                case "east":
                case "e": return BlockFacing.EAST;
                case "south":
                case "s": return BlockFacing.SOUTH;
                case "west":
                case "w": return BlockFacing.WEST;
                default: return null;
            }
        }

        private void OnServerPushTick(float dt)
        {
            if (Api?.Side != EnumAppSide.Server || Inventory == null || ioFaces == null) return;

            ItemSlot slot = Inventory[SlotOutput];
            if (slot == null || slot.Empty) return;

            foreach (FaceMapEntry entry in ioFaces.OutputEntries)
            {
                int moved = InventoryPusher.TryPush(Api.World, entry.Cell, entry.Face, slot, OutputPushBatch);
                if (moved > 0)
                {
                    MarkDirty(true);
                    if (slot.Empty) return;
                }
            }
        }

        private void OnKineticTick(float dt)
        {
            if (qpgField == null || playersGrindingField == null) return;

            BEBehaviorKinetic beh = GetBehavior<BEBehaviorKinetic>();
            if (beh == null) return;

            bool networkActive =
                !beh.NetworkConflicted &&
                MathF.Abs(beh.CurrentRPM) >= KineticNetwork.MinAbsRPM;

            int playerCount = (playersGrindingField.GetValue(this) as ICollection)?.Count ?? 0;
            int desired = networkActive ? Math.Max(playerCount, 1) : playerCount;
            int current = (int)qpgField.GetValue(this);
            if (current != desired) qpgField.SetValue(this, desired);
        }

        public override void OnBlockUnloaded()
        {
            topRenderer?.Dispose();
            topRenderer = null;
            base.OnBlockUnloaded();
        }

        public override void OnBlockRemoved()
        {
            topRenderer?.Dispose();
            topRenderer = null;
            base.OnBlockRemoved();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (!string.IsNullOrEmpty(placementFacingCode))
            {
                tree.SetString("placementFacing", placementFacingCode);
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            placementFacingCode = tree.GetString("placementFacing", placementFacingCode);
            if (Api != null) BuildIOFaceMap();
        }

        private void ReplaceVanillaRenderer(ICoreClientAPI capi)
        {
            if (capi == null) return;

            var vanillaRenderer = rendererField?.GetValue(this) as IRenderer;
            vanillaRenderer?.Dispose();
            rendererField?.SetValue(this, null);

            EnsureMeshes(capi, capi.Tesselator);
            if (topMesh == null) return;

            topRenderer = new KineticQuernTopRenderer(capi, Pos, topMesh, this);
            capi.Event.RegisterRenderer(topRenderer, EnumRenderStage.Opaque, "vintagekinematics:kineticquern");
        }

        private void EnsureMeshes(ICoreClientAPI capi, ITesselatorAPI tess)
        {
            if (baseMesh != null && topMesh != null) return;

            Shape shape = LoadShape(capi);
            if (shape == null) return;

            Shape baseShape = shape.Clone();
            baseShape.RemoveElements(new[] { "Stone" });
            tess.TesselateShape(Block, baseShape, out baseMesh);

            Shape topShape = shape.Clone();
            topShape.RemoveElements(new[] { "Base" });
            tess.TesselateShape(Block, topShape, out topMesh);
        }

        private Shape LoadShape(ICoreClientAPI capi)
        {
            if (Block?.Shape?.Base == null) return null;

            AssetLocation loc = Block.Shape.Base.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");
            return Shape.TryGet(capi, loc);
        }

        private bool IsKineticallyActive()
        {
            BEBehaviorKinetic beh = GetBehavior<BEBehaviorKinetic>();
            return beh != null
                && !beh.NetworkConflicted
                && MathF.Abs(beh.ActualRPM) >= KineticNetwork.MinAbsRPM;
        }

        private int QuantityPlayersGrinding()
        {
            if (qpgField == null) return 0;
            return (int)qpgField.GetValue(this);
        }

        private sealed class KineticQuernTopRenderer : IRenderer
        {
            private readonly ICoreClientAPI capi;
            private readonly BlockPos pos;
            private readonly BEKineticQuern quern;
            private readonly MeshRef meshRef;
            private readonly Matrixf modelMat = new Matrixf();

            public double RenderOrder => 0.5;
            public int RenderRange => 24;
            public float AngleRad { get; private set; }

            public KineticQuernTopRenderer(ICoreClientAPI capi, BlockPos pos, MeshData mesh, BEKineticQuern quern)
            {
                this.capi = capi;
                this.pos = pos;
                this.quern = quern;
                meshRef = capi.Render.UploadMesh(mesh);
            }

            public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
            {
                if (meshRef == null || capi.World.Player?.Entity == null) return;

                Vec3d camPos = capi.World.Player.Entity.CameraPos;
                IRenderAPI rpi = capi.Render;

                rpi.GlDisableCullFace();
                rpi.GlToggleBlend(true);

                IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
                prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
                prog.ModelMatrix = modelMat
                    .Identity()
                    .Translate((float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y), (float)(pos.Z - camPos.Z))
                    .Translate(0.5f, 0.5f, 0.5f)
                    .RotateY(AngleRad)
                    .Translate(-0.5f, -0.5f, -0.5f)
                    .Values;
                prog.ViewMatrix = rpi.CameraMatrixOriginf;
                prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
                rpi.RenderMesh(meshRef);
                prog.Stop();

                BEBehaviorKinetic beh = quern.GetBehavior<BEBehaviorKinetic>();
                float rpm = beh?.ActualRPM ?? 0f;
                if (quern.IsKineticallyActive())
                {
                    AngleRad += deltaTime * rpm / 60f * GameMath.TWOPI;
                }
                else if (quern.QuantityPlayersGrinding() > 0)
                {
                    AngleRad += deltaTime * 40f * GameMath.DEG2RAD;
                }
            }

            public void Dispose()
            {
                capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
                meshRef?.Dispose();
            }
        }
    }
}
