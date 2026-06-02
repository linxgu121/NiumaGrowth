using NiumaCore.Event;
using NiumaGrowth.Config;
using NiumaGrowth.Data;

namespace NiumaGrowth.Service
{
    /// <summary>
    /// 成长查询接口。只提供读能力，不暴露存档导出，避免 UI、剧情、技能条件等查询方拿到过宽权限。
    /// </summary>
    public interface IGrowthQuery
    {
        long Revision { get; }
        int GetLevel(string actorId, string skillId);
        int GetTotalExp(string actorId, string skillId);
        GrowthProgressViewData GetProgress(string actorId, string skillId);
        GrowthProgressViewData[] GetAllProgress(string actorId, bool includeNotStarted = true);
        bool MeetsRequirement(string actorId, GrowthRequirementData requirement);
    }

    /// <summary>
    /// 成长命令接口。所有会改变成长事实的入口都应该通过这里调用。
    /// </summary>
    public interface IGrowthCommand
    {
        GrowthOperationResult AddExp(GrowthExpRequest request);
        GrowthOperationResult SetExp(string actorId, string skillId, int totalExp, string sourceModule = null);
        GrowthOperationResult ResetProgress(string actorId, string skillId, string sourceModule = null);
        GrowthOperationResult ApplyInheritance(GrowthInheritanceRequest request);
        GrowthOperationResult ImportSnapshot(GrowthSaveData snapshot);
    }

    /// <summary>
    /// 成长服务门面。存档导出放在组合服务上，避免污染纯查询接口。
    /// </summary>
    public interface IGrowthService : IGrowthQuery, IGrowthCommand
    {
        GrowthSaveData ExportSnapshot();
    }

    /// <summary>
    /// 成长配置能力接口。Controller 热更新配置时使用，普通业务模块不应依赖它。
    /// </summary>
    public interface IGrowthConfigurationService
    {
        void SetDefinitions(GrowthSkillDefinition[] definitions);
        void SetEventBus(IEventBus eventBus);
    }
}
