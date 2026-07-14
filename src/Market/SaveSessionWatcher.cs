using UnityEngine;

namespace Routier
{
  /// <summary>
  /// Detects GameState.justStarted for new games (load games are handled in LoadModData).
  /// </summary>
  internal sealed class SaveSessionWatcher : MonoBehaviour
  {
    private bool _wasJustStarted;

    private void Update()
    {
      if (!GameState.justStarted)
      {
        _wasJustStarted = false;
        return;
      }

      if (_wasJustStarted)
        return;

      _wasJustStarted = true;
      DatabaseSession.OnNewGameStarted();
    }
  }
}
