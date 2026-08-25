using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VintageKinematics.BlockEntities;
using Xunit;

namespace VintageKinematics.Tests.Storage.Content
{
    public class WarehouseContentAssetTests
    {
        [Fact]
        public void WoodenCellRecipe_CraftsFourCellsFromPlanksAndNails()
        {
            using JsonDocument recipe = LoadRecipe("warehousecell-wood.json");
            JsonElement root = recipe.RootElement;

            Assert.Equal("PNP,NPN,PNP", root.GetProperty("ingredientPattern").GetString());
            Assert.Equal("*:plank-*", IngredientCode(root, "P"));
            Assert.Equal("game:metalnailsandstrips-*", IngredientCode(root, "N"));
            AssertOutput(root, "kineticwarehousecell-wood", 4);
        }

        [Fact]
        public void ReinforcedCellRecipe_UsesOneBandBatchAndTwoIronNails()
        {
            using JsonDocument recipe = LoadRecipe("warehousecell-reinforced.json");
            JsonElement root = recipe.RootElement;

            Assert.Equal("B_B,NCN,B_B", root.GetProperty("ingredientPattern").GetString());
            Assert.Equal("pressedband-iron", IngredientCode(root, "B"));
            Assert.Equal("game:metalnailsandstrips-iron", IngredientCode(root, "N"));
            Assert.Equal("kineticwarehousecell-wood", IngredientCode(root, "C"));
            AssertOutput(root, "kineticwarehousecell-reinforced", 1);
        }

        [Fact]
        public void TerminalRecipe_RequiresTemporalGearGritAndCopperFunnel()
        {
            using JsonDocument recipe = LoadRecipe("warehouseterminal.json");
            JsonElement root = recipe.RootElement;

            Assert.Equal("PGP,CTC,PFP", root.GetProperty("ingredientPattern").GetString());
            Assert.Equal("vintagekinematics:*-grit", IngredientCode(root, "G"));
            Assert.Equal("game:gear-temporal", IngredientCode(root, "T"));
            Assert.Equal("funnel-copper-north", IngredientCode(root, "F"));
            AssertOutput(root, "kineticwarehouseterminal-n", 1);
        }

        [Fact]
        public void WarehousePortRecipes_UpgradeWoodCellsWithForgePressedParts()
        {
            using JsonDocument beltInput = LoadRecipe("warehouseport-beltinput.json");
            using JsonDocument beltOutput = LoadRecipe("warehouseport-beltoutput.json");
            using JsonDocument driveInput = LoadRecipe("warehouseport-kineticinput.json");

            AssertBeltPortRecipe(
                beltInput.RootElement,
                "BFB,PCP,_M_",
                "kineticwarehouseport-beltinput-n");
            AssertBeltPortRecipe(
                beltOutput.RootElement,
                "_M_,PCP,BFB",
                "kineticwarehouseport-beltoutput-n");

            JsonElement drive = driveInput.RootElement;
            Assert.Equal("BMB,CWC,_S_", drive.GetProperty("ingredientPattern").GetString());
            Assert.Equal("pressedband-iron", IngredientCode(drive, "B"));
            Assert.Equal("machinebracket-iron", IngredientCode(drive, "M"));
            Assert.Equal("game:metalplate-copper", IngredientCode(drive, "C"));
            Assert.Equal("kineticwarehousecell-wood", IngredientCode(drive, "W"));
            Assert.Equal("shaft-y", IngredientCode(drive, "S"));
            AssertOutput(drive, "kineticwarehouseport-kineticinput-n", 1);
        }

        [Fact]
        public void WarehouseGuide_IsRegisteredAndMerged()
        {
            string root = FindProjectRoot();
            string page = File.ReadAllText(Path.Combine(
                root,
                "assets/vintagekinematics/config/handbook/08-kinetic-warehouse.json"));
            Assert.Contains("vintagekinematics:vkguide-kinetic-warehouse", page);

            using JsonDocument language = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                root,
                "assets/vintagekinematics/lang/en.json")));
            Assert.True(language.RootElement.TryGetProperty(
                "vintagekinematics:vkguide-kinetic-warehouse-text",
                out JsonElement text));
            Assert.Contains("searchable storage built from physical blocks", text.GetString());
            Assert.Contains("Drag the bottom edge", text.GetString());
            Assert.Contains("Gear the shaft down to 16 RPM", text.GetString());

            using JsonDocument handbook = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                root,
                "langsrc/en/65-warehouse-handbook.json")));
            string drivePortText = handbook.RootElement.GetProperty(
                "vintagekinematics:block-handbooktext-vintagekinematics:kineticwarehouseport-kineticinput-*")
                .GetString();
            Assert.Contains("Gear the input down to 16 RPM", drivePortText);
            Assert.Contains("does not make storage transfers faster", drivePortText);
        }

        [Fact]
        public void DrivePort_AloneCarriesKineticConsumerBehavior()
        {
            using JsonDocument asset = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                FindProjectRoot(),
                "assets/vintagekinematics/blocktypes/kineticwarehouseport.json")));
            JsonElement behaviors = asset.RootElement.GetProperty("entityBehaviorsByType");

            Assert.Equal(4, behaviors.EnumerateObject().Count());
            foreach (JsonProperty variant in behaviors.EnumerateObject())
            {
                Assert.Contains("kineticinput", variant.Name);
                Assert.Equal("Kinetic", variant.Value[0].GetProperty("name").GetString());
            }
            Assert.Equal(
                16,
                asset.RootElement
                    .GetProperty("attributes")
                    .GetProperty("vkKinetic")
                    .GetProperty("stressImpact")
                    .GetInt32());
            Assert.Equal(
                250,
                asset.RootElement
                    .GetProperty("attributes")
                    .GetProperty("vkStorageOutputIntervalMs")
                    .GetInt32());
        }

        [Fact]
        public void PortShapes_RotateEveryInterfaceToItsSideVariant()
        {
            using JsonDocument asset = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                FindProjectRoot(),
                "assets/vintagekinematics/blocktypes/kineticwarehouseport.json")));
            JsonElement shapes = asset.RootElement.GetProperty("shapeByType");
            string[] ports = { "beltinput", "beltoutput", "kineticinput" };
            (string Side, int Rotation)[] sides =
            {
                ("n", 0),
                ("e", 90),
                ("s", 180),
                ("w", 270),
            };

            Assert.Equal(12, shapes.EnumerateObject().Count());
            foreach (string port in ports)
            {
                foreach ((string side, int rotation) in sides)
                {
                    JsonElement shape = shapes.GetProperty($"*-{port}-{side}");
                    Assert.Contains($"storageport-{PortShapeName(port)}-north", shape.GetProperty("base").GetString());
                    Assert.Equal(
                        rotation,
                        shape.TryGetProperty("rotateY", out JsonElement rotateY)
                            ? rotateY.GetInt32()
                            : 0);
                }
            }
        }

        [Fact]
        public void BeltInputShape_MouthSurroundsPhysicalBeltDeck()
        {
            using JsonDocument shape = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                FindProjectRoot(),
                "assets/vintagekinematics/shapes/block/storage/storageport-belt-input-north.json")));
            JsonElement mouth = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Single(element => element.GetProperty("name").GetString() == "belt-input-mouth");
            double bottom = mouth.GetProperty("from")[1].GetDouble();
            double top = mouth.GetProperty("to")[1].GetDouble();
            double beltDeck = BEBelt.BeltTopY * 16d;

            Assert.True(bottom < beltDeck);
            Assert.True(top > beltDeck);
            Assert.True((bottom + top) / 2d > 10d);

            string[] elementNames = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Select(element => element.GetProperty("name").GetString())
                .ToArray();
            Assert.Equal(
                4,
                elementNames.Count(name => name.StartsWith("belt-input-direction-")));
            Assert.DoesNotContain("belt-input-direction-line", elementNames);
            Assert.DoesNotContain("belt-input-direction-tail", elementNames);

            JsonElement[] chevrons = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Where(element => element.GetProperty("name").GetString()
                    .StartsWith("belt-input-direction-"))
                .ToArray();
            Assert.All(chevrons, element =>
            {
                Assert.Equal(3d, element.GetProperty("to")[0].GetDouble()
                    - element.GetProperty("from")[0].GetDouble(), 3);
                Assert.True(Math.Abs(element.GetProperty("rotationZ").GetDouble()) >= 30d);
            });

            JsonElement[] hood = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Where(element => element.GetProperty("name").GetString()
                    .StartsWith("belt-input-hood-"))
                .ToArray();
            Assert.Equal(3, hood.Length);
            Assert.All(hood, element => Assert.Equal(-3.5d, element.GetProperty("from")[2].GetDouble()));

            JsonElement[] throat = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Where(element => element.GetProperty("name").GetString()
                    .StartsWith("belt-input-throat-"))
                .ToArray();
            Assert.Equal(4, throat.Length);
            Assert.All(throat, element =>
            {
                Assert.Equal(0d, element.GetProperty("from")[2].GetDouble());
                Assert.Equal(0.99d, element.GetProperty("to")[2].GetDouble());
            });

            JsonElement[] openFaceEdgeRails = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Where(element => element.GetProperty("name").GetString()
                    .StartsWith("edge-z-"))
                .ToArray();
            Assert.Equal(4, openFaceEdgeRails.Length);
            Assert.All(openFaceEdgeRails, element =>
                Assert.True(element.GetProperty("faces").TryGetProperty("north", out _)));

            AssertNorthExtrusions(
                shape.RootElement.GetProperty("elements").EnumerateArray().ToArray(),
                "belt-input-plate",
                "belt-input-recess");

            JsonElement bulkhead = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Single(element => element.GetProperty("name").GetString()
                    == "belt-input-front-bulkhead");
            Assert.True(
                bulkhead.GetProperty("to")[2].GetDouble()
                - bulkhead.GetProperty("from")[2].GetDouble()
                >= 0.5d);

            JsonElement downPanel = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Single(element => element.GetProperty("name").GetString()
                    == "down-framed-panel");
            Assert.Equal(1.25d, downPanel.GetProperty("from")[0].GetDouble());
            Assert.Equal(14.75d, downPanel.GetProperty("to")[0].GetDouble());
        }

        [Fact]
        public void ConcaveElbowShapes_CloseBothAxialEndsWithRecessedCaps()
        {
            string shapeDirectory = Path.Combine(
                FindProjectRoot(),
                "assets/vintagekinematics/shapes/block/storage");
            string[] shapePaths = Directory.GetFiles(
                shapeDirectory,
                "storagecell-elbow-*.json");

            Assert.Equal(12, shapePaths.Length);
            foreach (string shapePath in shapePaths)
            {
                using JsonDocument shape = JsonDocument.Parse(File.ReadAllText(shapePath));
                JsonElement[] caps = shape.RootElement
                    .GetProperty("elements")
                    .EnumerateArray()
                    .Where(element => element.GetProperty("name").GetString()
                        .Contains("-cap-"))
                    .ToArray();

                Assert.Equal(2, caps.Length);
                Assert.All(caps, cap =>
                {
                    Assert.Single(cap.GetProperty("faces").EnumerateObject());
                    double[] from = cap.GetProperty("from").EnumerateArray()
                        .Select(value => value.GetDouble()).ToArray();
                    double[] to = cap.GetProperty("to").EnumerateArray()
                        .Select(value => value.GetDouble()).ToArray();
                    Assert.True(
                        (from.Contains(0.01d) && to.Contains(0.02d))
                        || (from.Contains(15.98d) && to.Contains(15.99d)));
                });
            }
        }

        [Fact]
        public void BeltOutputShape_UsesRaisedSteppedDischargeChute()
        {
            using JsonDocument shape = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                FindProjectRoot(),
                "assets/vintagekinematics/shapes/block/storage/storageport-belt-output-north.json")));
            JsonElement[] elements = shape.RootElement.GetProperty("elements").EnumerateArray().ToArray();
            JsonElement[] chute = elements
                .Where(element => element.GetProperty("name").GetString()
                    .StartsWith("belt-output-chute-"))
                .ToArray();
            JsonElement[] arrow = elements
                .Where(element => element.GetProperty("name").GetString()
                    .StartsWith("belt-output-arrow-"))
                .ToArray();

            Assert.Equal(9, chute.Length);
            Assert.DoesNotContain(
                chute,
                element => element.GetProperty("name").GetString().Contains("bottom"));
            Assert.Equal(
                3,
                chute.Count(element => element.GetProperty("name").GetString()
                    .StartsWith("belt-output-chute-shoulder-")));
            Assert.All(
                chute.Where(element => element.GetProperty("name").GetString()
                    .EndsWith("left") || element.GetProperty("name").GetString().EndsWith("right")),
                element => Assert.True(
                    element.GetProperty("from")[1].GetDouble() > BEBelt.BeltTopY * 16d));
            Assert.Contains(chute, element => element.GetProperty("from")[2].GetDouble() == -3d);

            Assert.Equal(3, arrow.Length);
            Assert.All(arrow, element =>
            {
                JsonProperty face = Assert.Single(element.GetProperty("faces").EnumerateObject());
                Assert.Equal("up", face.Name);
            });
            Assert.DoesNotContain(
                elements,
                element => element.GetProperty("name").GetString()
                    .StartsWith("belt-output-direction-"));
            AssertNorthExtrusions(
                elements,
                "belt-output-plate",
                "belt-output-recess");
        }

        [Fact]
        public void ConnectedCellPanel_ExtendsOnlyAcrossUnframedJoinedEdges()
        {
            using JsonDocument shape = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                FindProjectRoot(),
                "assets/vintagekinematics/shapes/block/storage/storagecell-mask-ew.json")));
            JsonElement topPanel = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Single(element => element.GetProperty("name").GetString()
                    == "up-exterior-panel");

            Assert.Equal(0d, topPanel.GetProperty("from")[0].GetDouble());
            Assert.Equal(16d, topPanel.GetProperty("to")[0].GetDouble());
            Assert.Equal(1.25d, topPanel.GetProperty("from")[2].GetDouble());
            Assert.Equal(14.75d, topPanel.GetProperty("to")[2].GetDouble());

            JsonElement topFrontRail = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Single(element => element.GetProperty("name").GetString()
                    == "edge-x-up-north");
            Assert.Equal(0d, topFrontRail.GetProperty("from")[0].GetDouble());
            Assert.Equal(16d, topFrontRail.GetProperty("to")[0].GetDouble());

            JsonElement westCap = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Single(element => element.GetProperty("name").GetString()
                    == "edge-x-up-north-cap-west");
            JsonElement eastCap = shape.RootElement
                .GetProperty("elements")
                .EnumerateArray()
                .Single(element => element.GetProperty("name").GetString()
                    == "edge-x-up-north-cap-east");
            Assert.Equal(0.01d, westCap.GetProperty("from")[0].GetDouble());
            Assert.Equal(15.99d, eastCap.GetProperty("to")[0].GetDouble());
        }

        private static JsonDocument LoadRecipe(string fileName)
        {
            return JsonDocument.Parse(File.ReadAllText(Path.Combine(
                FindProjectRoot(),
                "assets/vintagekinematics/recipes/grid",
                fileName)));
        }

        private static void AssertNorthExtrusions(
            JsonElement[] elements,
            params string[] elementNames)
        {
            foreach (string elementName in elementNames)
            {
                JsonElement faces = elements
                    .Single(element => element.GetProperty("name").GetString() == elementName)
                    .GetProperty("faces");
                string[] faceNames = faces.EnumerateObject().Select(face => face.Name).ToArray();
                Assert.Contains("north", faceNames);
                Assert.Contains("west", faceNames);
                Assert.Contains("east", faceNames);
                Assert.Contains("down", faceNames);
                Assert.Contains("up", faceNames);
                Assert.DoesNotContain("south", faceNames);
            }
        }

        private static string IngredientCode(JsonElement root, string key)
        {
            return root
                .GetProperty("ingredients")
                .GetProperty(key)
                .GetProperty("code")
                .GetString();
        }

        private static void AssertBeltPortRecipe(
            JsonElement root,
            string pattern,
            string outputCode)
        {
            Assert.Equal(pattern, root.GetProperty("ingredientPattern").GetString());
            Assert.Equal("belt", IngredientCode(root, "B"));
            Assert.Equal("funnel-copper-north", IngredientCode(root, "F"));
            Assert.Equal("pressedband-iron", IngredientCode(root, "P"));
            Assert.Equal("kineticwarehousecell-wood", IngredientCode(root, "C"));
            Assert.Equal("machinebracket-iron", IngredientCode(root, "M"));
            AssertOutput(root, outputCode, 1);
        }

        private static void AssertOutput(JsonElement root, string code, int quantity)
        {
            JsonElement output = root.GetProperty("output");
            Assert.Equal(code, output.GetProperty("code").GetString());
            Assert.Equal(quantity, output.GetProperty("quantity").GetInt32());
        }

        private static string PortShapeName(string port)
        {
            return port switch
            {
                "beltinput" => "belt-input",
                "beltoutput" => "belt-output",
                "kineticinput" => "kinetic-input",
                _ => throw new ArgumentOutOfRangeException(nameof(port)),
            };
        }

        private static string FindProjectRoot()
        {
            string directory = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "VintageKinematics.csproj")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not find VintageKinematics project root.");
        }
    }
}
