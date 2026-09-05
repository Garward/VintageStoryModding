namespace VRPG.Modules.Rpg.Players;

/// <summary>Reads and writes RPG progression owned by one world save.</summary>
public interface IRpgPlayerStateStorage
{
    byte[]? Read();

    void Write(byte[] data);
}
