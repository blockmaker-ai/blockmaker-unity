using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blockmaker;

[CreateAssetMenu(fileName = "DefaultAvatars", menuName = "Blockmaker/Default Avatar Set")]
public class DefaultAvatarSet : ScriptableObject
{
    public List<DefaultAvatarEntry> avatars = new List<DefaultAvatarEntry>();
}

[Serializable]
public class DefaultAvatarEntry
{
    public string id;
    public string name;
    public Sprite thumbnail;
}
