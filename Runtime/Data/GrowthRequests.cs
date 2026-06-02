using System;

namespace NiumaGrowth.Data
{
    /// <summary>
    /// 增加技艺经验请求。
    /// </summary>
    [Serializable]
    public sealed class GrowthExpRequest
    {
        public string ActorId;
        public string SkillId;
        public int Amount;
        public string SourceModule;
        public string Reason;
    }

    /// <summary>
    /// 技艺传承请求。
    /// </summary>
    [Serializable]
    public sealed class GrowthInheritanceRequest
    {
        public string SourceActorId;
        public string TargetActorId;
        public float ExpMultiplier = 1f;
        public string SourceModule;
    }

    /// <summary>
    /// 成长操作结果。
    /// </summary>
    [Serializable]
    public sealed class GrowthOperationResult
    {
        public bool Succeeded;
        public GrowthFailureReason FailureReason = GrowthFailureReason.None;
        public string Message;
        public string ActorId;
        public string SkillId;
        public GrowthProgressViewData ChangedProgress;

        public static GrowthOperationResult Success(string actorId = null, string skillId = null, GrowthProgressViewData changed = null, string message = null)
        {
            return new GrowthOperationResult
            {
                Succeeded = true,
                FailureReason = GrowthFailureReason.None,
                Message = message,
                ActorId = actorId,
                SkillId = skillId,
                ChangedProgress = changed?.Clone()
            };
        }

        public static GrowthOperationResult Failed(GrowthFailureReason reason, string actorId = null, string skillId = null, string message = null)
        {
            return new GrowthOperationResult
            {
                Succeeded = false,
                FailureReason = reason,
                Message = message,
                ActorId = actorId,
                SkillId = skillId
            };
        }
    }
}
