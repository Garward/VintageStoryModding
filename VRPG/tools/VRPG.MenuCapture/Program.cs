if (System.Environment.GetEnvironmentVariable("VRPG_MENU_CAPTURE_MODE") == "party") VRPG.Tests.PartyCompositionCapture.Run();
else new VRPG.Tests.InventoryCompositionCaptureTests().CaptureActualShellFromRealStoreWhenRequested();
System.Console.WriteLine("Captured the production Cairo shell. GL models, native text fields, resolved bindings, input and joined-client behavior are not included.");
