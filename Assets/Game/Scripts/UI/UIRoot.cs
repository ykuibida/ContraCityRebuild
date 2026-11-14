// Draws the lobby or the battle interface depending on GameFlow state.
using UnityEngine;

public class UIRoot : MonoBehaviour
{
    readonly OptionsWindow options = new OptionsWindow();
    LobbyUI lobby;
    BattleUI battle;
    public static UIRoot Instance { get; private set; }
    public LobbyUI Lobby => lobby;
    public BattleUI Battle => battle;
    public OptionsWindow Options => options;

    void Awake()
    {
        Instance = this;
        lobby = new LobbyUI(options);
        battle = new BattleUI(options);
        lobby.Preview = gameObject.AddComponent<CharacterPreview>();
    }

    void Update()
    {
        battle.Update();
        options.Update();
        LoadingMapPopup.Update();
        if (GameFlow.Instance != null && GameFlow.Instance.Current == GameFlow.State.Lobby) ContraChat.Tick();   // MainMenu.Update → 嘑.Tick
        var st = GameFlow.Instance != null ? GameFlow.Instance.Current : GameFlow.State.Battle;
        bool lobbyScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == GameFlow.LobbyScene;
        if (lobby.Preview) lobby.Preview.Active = lobbyScene && (st == GameFlow.State.Lobby || st == GameFlow.State.Loading) && (lobby.mainTab <= 3 || lobby.mainTab == 7) && !lobby.mapSelect;   // New Era keeps the HQ room behind the gym / workshop / rename windows (ne_ui2/013_hq_gym)
    }

    void OnGUI()
    {
        var flow = GameFlow.Instance;
        if (flow == null) return;
        GUI.depth = 0;
        if (flow.Current == GameFlow.State.Battle) battle.Draw();
        else if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == GameFlow.LobbyScene)
        {
            GUI.enabled = flow.Current == GameFlow.State.Lobby;   // while loading the popup sits over a frozen HQ
            lobby.Draw();
            GUI.enabled = true;
        }
        LoadingMapPopup.Draw();
    }
}
