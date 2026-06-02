namespace NiumaGrowth.Data
{
    /// <summary>
    /// 技艺经验发生变化时发布。用于自动存档、剧情监听、调试日志等低频系统。
    /// </summary>
    public readonly struct GrowthExpChangedEvent
    {
        public readonly string ActorId;
        public readonly string SkillId;
        public readonly int OldTotalExp;
        public readonly int NewTotalExp;
        public readonly int OldLevel;
        public readonly int NewLevel;
        public readonly string SourceModule;

        public GrowthExpChangedEvent(string actorId, string skillId, int oldTotalExp, int newTotalExp, int oldLevel, int newLevel, string sourceModule)
        {
            ActorId = actorId;
            SkillId = skillId;
            OldTotalExp = oldTotalExp;
            NewTotalExp = newTotalExp;
            OldLevel = oldLevel;
            NewLevel = newLevel;
            SourceModule = sourceModule;
        }
    }

    /// <summary>
    /// 技艺等级发生变化时发布。等级由 TotalExp 和当前配置阈值计算，不进入存档。
    /// </summary>
    public readonly struct GrowthLevelChangedEvent
    {
        public readonly string ActorId;
        public readonly string SkillId;
        public readonly int OldLevel;
        public readonly int NewLevel;
        public readonly string SourceModule;

        public GrowthLevelChangedEvent(string actorId, string skillId, int oldLevel, int newLevel, string sourceModule)
        {
            ActorId = actorId;
            SkillId = skillId;
            OldLevel = oldLevel;
            NewLevel = newLevel;
            SourceModule = sourceModule;
        }
    }

    /// <summary>
    /// 技艺进度被重置时发布。
    /// </summary>
    public readonly struct GrowthProgressResetEvent
    {
        public readonly string ActorId;
        public readonly string SkillId;
        public readonly string SourceModule;

        public GrowthProgressResetEvent(string actorId, string skillId, string sourceModule)
        {
            ActorId = actorId;
            SkillId = skillId;
            SourceModule = sourceModule;
        }
    }

    /// <summary>
    /// 技艺传承产生经验变化时发布。
    /// </summary>
    public readonly struct GrowthInheritanceAppliedEvent
    {
        public readonly string SourceActorId;
        public readonly string TargetActorId;
        public readonly float ExpMultiplier;
        public readonly string SourceModule;

        public GrowthInheritanceAppliedEvent(string sourceActorId, string targetActorId, float expMultiplier, string sourceModule)
        {
            SourceActorId = sourceActorId;
            TargetActorId = targetActorId;
            ExpMultiplier = expMultiplier;
            SourceModule = sourceModule;
        }
    }
}
