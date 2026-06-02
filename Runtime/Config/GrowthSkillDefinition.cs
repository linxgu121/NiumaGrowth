using System;
using UnityEngine;

namespace NiumaGrowth.Config
{
    /// <summary>
    /// 技艺成长配置。等级由 TotalExp 对照 LevelThresholds 计算，存档不保存等级。
    /// </summary>
    [CreateAssetMenu(menuName = "NiumaGrowth/Growth Skill Definition", fileName = "GrowthSkillDefinition")]
    public sealed class GrowthSkillDefinition : ScriptableObject
    {
        [Tooltip("技艺稳定 ID。用于存档、任务、技能条件和调试。一旦进入正式内容不要改名。")]
        public string SkillId;

        [Tooltip("技艺显示名称。后续接本地化时可以改成本地化 Key。")]
        public string DisplayName;

        [Tooltip("技艺说明。用于 UI、调试或策划查看。")]
        [TextArea]
        public string Description;

        [Tooltip("图标 Addressables Key 或资源路径。")]
        public string IconAddress;

        [Tooltip("技艺分类。用于 UI 筛选，不参与数值计算。")]
        public GrowthCategory Category = GrowthCategory.Craft;

        [Tooltip("等级阈值。Level 从 0 开始，TotalExp >= LevelThresholds[n] 时达到 n+1 级。必须递增。")]
        public int[] LevelThresholds = Array.Empty<int>();

        [Tooltip("是否参与传承。关闭后 ApplyInheritance 不会把该技艺复制到目标 Actor。")]
        public bool IsInheritable = true;

        private void OnValidate()
        {
            ValidateLevelThresholds();
        }

        /// <summary>
        /// 校验等级阈值。这里只做编辑器提示，不自动重排，避免悄悄改变策划填写的数据。
        /// </summary>
        private void ValidateLevelThresholds()
        {
            if (LevelThresholds == null || LevelThresholds.Length == 0)
            {
                return;
            }

            var previous = -1;
            for (var i = 0; i < LevelThresholds.Length; i++)
            {
                var threshold = LevelThresholds[i];
                if (threshold < 0)
                {
                    Debug.LogError($"[NiumaGrowth] {name} 的 LevelThresholds[{i}] 为负数，运行时会忽略该配置。", this);
                    return;
                }

                if (threshold <= previous)
                {
                    Debug.LogError($"[NiumaGrowth] {name} 的 LevelThresholds 必须严格递增，运行时会忽略该配置。", this);
                    return;
                }

                previous = threshold;
            }
        }
    }

    /// <summary>
    /// 技艺分类。None 只用于默认值保护，正式配置不要使用。
    /// </summary>
    public enum GrowthCategory
    {
        None = 0,
        Craft = 1,
        Combat = 2,
        Scholarship = 3,
        Social = 4,
        Exploration = 5,
        Custom = 100
    }
}
