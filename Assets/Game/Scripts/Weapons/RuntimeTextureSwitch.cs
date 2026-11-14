// Port of the original RuntimeTextureSwitch (New Era NE_Scripts_Decrypted/RuntimeTextureSwitch.cs; Penta 2016 the same).
// Same class name, fields and script GUID (c6d30dc9b55b8d493c434372211558c3): the katana / scythe blades and the snowgun.
// Switch(0) = the default texture, Switch(n) = entry n of the comma list (CombatWeapon.SwitchWeaponTexture, NE 噔.cs:1038-1048).
public class RuntimeTextureSwitch : RuntimeTextureLoader
{
    public string textureIDList = string.Empty;

    public void Switch(int a)
    {
        if (a == 0) { LoadTexture(textureID); return; }
        string[] list = textureIDList.Split(',');
        if (a <= list.Length) LoadTexture(list[a - 1]);
    }
}
