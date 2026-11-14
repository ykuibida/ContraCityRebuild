// Port of the original AutomaticDoor (Penta 2016 AutomaticDoor.cs; New Era NE_Scripts_Decrypted/AutomaticDoor.cs, offline path).
// Same class name, fields and script GUID (c8404a65286431815bd8ec125accb54a) as the original, so the doors stored in the map scenes
// (base gates "steel_03_b01" with Team 1 / 2 on Angar, ArenaRing, Arena_3lvl, Arena_well, LegoTurnament, TF3_Arena_map, Zombi_2,
// and the "Door_animation" template) bind to it with their data.
//   • after a 200 ms launch delay, every 500 ms: PlayerManager.TeamScan(scanner, touchDistance, OtherTeam(Team), false, true) —
//     a live player within touchDistance of the "Scanner" child (local player included) who is not in the other team
//     (DM players are team 0, so everybody opens every door);
//   • somebody found and the door closed → play OpenAnimationName, nobody (-1) and open → CloseAnimationName; then 1 s pause;
//   • the animation speed is doorSpeed. Dropped: anti-cheat (SendEnterBaseRequest 9) and the network door sync (NE).
using UnityEngine;

public class AutomaticDoor : MonoBehaviour
{
    public float touchDistance = 20f;
    public float doorSpeed = 2f;
    public string OpenAnimationName;
    public string CloseAnimationName;
    public byte Team;

    const long LaunchDelay = 200;
    long launchTime, nextScanTime;
    bool active, doorOpened;
    public bool IsOpen => doorOpened;
    Animation anim;
    Vector3 scannerPos;

    static long Now => (long)(Time.time * 1000f);   // TimeManager.NetworkTime (ms)

    void Start()
    {
        var parent = transform.parent;
        if (parent == null) { enabled = false; return; }
        anim = parent.GetComponentInChildren<Animation>();
        var scanner = parent.Find("Scanner");
        scannerPos = scanner != null ? scanner.position : parent.position;
        launchTime = Now + LaunchDelay;
    }

    static int OtherTeam(int team) => team == 0 ? 0 : team == 1 ? 2 : 1;

    void FixedUpdate()
    {
        if (anim == null) return;
        long time = Now;
        if (time >= launchTime || launchTime == 0) active = true;
        if (!active || (nextScanTime >= time && nextScanTime != 0)) return;
        nextScanTime = time + 500;
        int found = TeamScan(scannerPos, touchDistance, OtherTeam(Team));
        if (found > 0 && !doorOpened) { Play(OpenAnimationName); doorOpened = true; nextScanTime = time + 1000; }
        else if (found == -1 && doorOpened) { Play(CloseAnimationName); doorOpened = false; nextScanTime = time + 1000; }
    }

    /// <summary>PlayerManager.TeamScan (NE :10143-10165): id of the first live player closer than <paramref name="dist"/> whose team
    /// is not <paramref name="excludedTeam"/> (team 0 always counts), the local player included; -1 if none.</summary>
    static int TeamScan(Vector3 pos, float dist, int excludedTeam)
    {
        var flow = GameFlow.Instance;
        if (flow == null || flow.Current != GameFlow.State.Battle || !flow.Player) return -1;
        var v = flow.Player.GetComponent<PlayerVitals>();
        if (v == null || v.Dead) return -1;
        int team = flow.LocalTeam;
        if (team == excludedTeam && team != 0) return -1;
        return Vector3.Distance(flow.Player.transform.position, pos) < dist ? GameFlow.LocalId : -1;
    }

    void Play(string clip)
    {
        if (anim == null || string.IsNullOrEmpty(clip)) return;
        anim.Stop();
        anim.Play(clip);
        if (anim[clip] != null) anim[clip].speed = doorSpeed;
    }
}
