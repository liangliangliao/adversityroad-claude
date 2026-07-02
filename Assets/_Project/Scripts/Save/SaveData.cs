using System;
using AdversityRoad.Personalization;
using AdversityRoad.Player;

namespace AdversityRoad.Save
{
    [Serializable]
    public class SaveData
    {
        public PlayerProfile profile;
        public PlayerStats stats;
        public string[] unlockedSkillIds;
        public string lastSceneId;
        public string savedAtUtc;
    }
}
