using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace EchoNav.Nav;

/// Reads which way the camera is pointing.
public static unsafe class CameraReader
{
    /// Camera heading in radians, or null if it can't be read - during a loading screen, for instance, when
    /// there is no active camera at all.
    public static float? Heading
    {
        get
        {
            try
            {
                var manager = CameraManager.Instance();
                if (manager == null)
                    return null;

                var camera = manager->GetActiveCamera();
                return camera == null ? null : camera->DirH;
            }
            catch
            {
                return null;
            }
        }
    }
}
