using System.Collections.Generic;

namespace VRPG.Data.Library;

public interface ILibrarySource
{
    string Code { get; }

    IEnumerable<LibraryEntry> Build(VRPGDataRegistry data);
}
