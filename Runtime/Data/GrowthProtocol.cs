using System;
using NiumaGrowth.Config;

namespace NiumaGrowth.Data
{
    /// <summary>
    /// 成长系统失败原因。UI 应按枚举做本地化，不要匹配 Message 字符串。
    /// </summary>
    public enum GrowthFailureReason
    {
        None = 0,
        InvalidRequest = 1,
        ActorMissing = 2,
        DefinitionMissing = 3,
        ImportInvalid = 4,
        ServiceNotReady = 5
    }

    /// <summary>
    /// UI 更新类型。第一版以全量刷新为主。
    /// </summary>
    public enum GrowthUIUpdateType
    {
        Refresh = 0,
        Cleared = 1
    }

    /// <summary>
    /// 技艺等级要求。
    /// </summary>
    [Serializable]
    public sealed class GrowthRequirementData
    {
        public string SkillId;
        public int RequiredLevel;
    }

    /// <summary>
    /// 单个技艺的存档快照。只保存 TotalExp，不保存等级。
    /// </summary>
    [Serializable]
    public sealed class GrowthProgressSnapshot
    {
        public string SkillId;
        public int TotalExp;
        public bool IsMissingDefinition;

        public GrowthProgressSnapshot Clone()
        {
            return new GrowthProgressSnapshot
            {
                SkillId = SkillId,
                TotalExp = TotalExp,
                IsMissingDefinition = IsMissingDefinition
            };
        }

        public static GrowthProgressSnapshot[] CloneArray(GrowthProgressSnapshot[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<GrowthProgressSnapshot>();
            }

            var result = new GrowthProgressSnapshot[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }

    /// <summary>
    /// 单个 Actor 的技艺成长快照。
    /// </summary>
    [Serializable]
    public sealed class GrowthOwnerSnapshot
    {
        public string ActorId;
        public GrowthProgressSnapshot[] Skills = Array.Empty<GrowthProgressSnapshot>();

        public GrowthOwnerSnapshot Clone()
        {
            return new GrowthOwnerSnapshot
            {
                ActorId = ActorId,
                Skills = GrowthProgressSnapshot.CloneArray(Skills)
            };
        }

        public static GrowthOwnerSnapshot[] CloneArray(GrowthOwnerSnapshot[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<GrowthOwnerSnapshot>();
            }

            var result = new GrowthOwnerSnapshot[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }

    /// <summary>
    /// 成长模块存档数据。
    /// </summary>
    [Serializable]
    public sealed class GrowthSaveData
    {
        public int Version = 1;
        public long Revision;
        public GrowthOwnerSnapshot[] Owners = Array.Empty<GrowthOwnerSnapshot>();

        public GrowthSaveData Clone()
        {
            return new GrowthSaveData
            {
                Version = Version,
                Revision = Revision,
                Owners = GrowthOwnerSnapshot.CloneArray(Owners)
            };
        }
    }

    /// <summary>
    /// UI 使用的单个技艺表现数据。
    /// </summary>
    [Serializable]
    public sealed class GrowthProgressViewData
    {
        public string SkillId;
        public string DisplayName;
        public string Description;
        public string IconAddress;
        public GrowthCategory Category;
        public int Level;
        public int TotalExp;
        public int CurrentLevelExp;
        public int NextLevelExp;
        public int ExpInCurrentLevel;
        public int ExpToNextLevel;
        public float Progress01;
        public bool IsMaxLevel;
        public bool IsMissingDefinition;
        public bool IsInheritable;

        public GrowthProgressViewData Clone()
        {
            return (GrowthProgressViewData)MemberwiseClone();
        }

        public static GrowthProgressViewData[] CloneArray(GrowthProgressViewData[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<GrowthProgressViewData>();
            }

            var result = new GrowthProgressViewData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }

    /// <summary>
    /// 某个 Actor 的成长面板表现数据。
    /// </summary>
    [Serializable]
    public sealed class GrowthPanelViewData
    {
        public string ActorId;
        public long Revision;
        public GrowthProgressViewData[] Skills = Array.Empty<GrowthProgressViewData>();

        public GrowthPanelViewData Clone()
        {
            return new GrowthPanelViewData
            {
                ActorId = ActorId,
                Revision = Revision,
                Skills = GrowthProgressViewData.CloneArray(Skills)
            };
        }
    }

    /// <summary>
    /// 成长 UI 更新包。
    /// </summary>
    public readonly struct GrowthUIUpdate
    {
        public readonly GrowthUIUpdateType UpdateType;
        public readonly long Revision;
        public readonly GrowthPanelViewData Current;
        public readonly GrowthPanelViewData Previous;

        public GrowthUIUpdate(GrowthUIUpdateType updateType, long revision, GrowthPanelViewData current, GrowthPanelViewData previous)
        {
            UpdateType = updateType;
            Revision = revision;
            Current = current;
            Previous = previous;
        }
    }

    /// <summary>
    /// 成长 UI 接收端。具体 UI 预制体由 UI 模块或策划制作。
    /// </summary>
    public interface IGrowthUIReceiver
    {
        void ApplyGrowthUpdate(GrowthUIUpdate update);
    }
}
