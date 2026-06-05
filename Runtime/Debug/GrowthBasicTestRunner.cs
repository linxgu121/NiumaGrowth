using System;
using System.Collections.Generic;
using NiumaCore.Event;
using NiumaGrowth.Config;
using NiumaGrowth.Data;
using NiumaGrowth.Service;
using UnityEngine;

namespace NiumaGrowth.Debugging
{
    /// <summary>
    /// NiumaGrowth 基础测试入口。
    /// 该组件只用于开发阶段在 Unity 场景内手动验证核心流程，不参与正式业务。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GrowthBasicTestRunner : MonoBehaviour
    {
        private const string ActorId = "player";
        private const string TargetActorId = "apprentice";
        private const string CraftSkillId = "craft_woodwork";
        private const string CombatSkillId = "combat_sword";
        private const string ScholarshipSkillId = "scholarship_genealogy";

        [Header("测试行为")]
        [Tooltip("运行测试后是否在 Console 输出每一条通过信息。关闭后只输出最终结果和失败原因。")]
        [SerializeField] private bool verboseLog = true;

        [Header("最近一次结果")]
        [Tooltip("最近一次基础测试是否全部通过。")]
        [SerializeField] private bool lastRunSucceeded;

        [Tooltip("最近一次通过的检查数量。")]
        [SerializeField] private int passedCheckCount;

        [Tooltip("最近一次失败的检查数量。")]
        [SerializeField] private int failedCheckCount;

        [Tooltip("最近一次测试报告。")]
        [TextArea(8, 24)]
        [SerializeField] private string lastReport;

        private readonly List<string> _reportLines = new List<string>();
        private readonly List<ScriptableObject> _createdAssets = new List<ScriptableObject>();

        /// <summary>
        /// 运行第 7 阶段基础测试。
        /// </summary>
        [ContextMenu("NiumaGrowthTest/运行基础测试")]
        public void RunBasicTests()
        {
            ResetReport();

            RunCase("增加经验后等级正确提升", TestAddExpAndLevel);
            RunCase("SetExp 正常设置经验", TestSetExp);
            RunCase("导出导入后经验等级Revision一致", TestExportImportRoundTrip);
            RunCase("阈值调整后从 TotalExp 重新计算等级", TestThresholdRecalculateAfterImport);
            RunCase("缺失配置导入保护", TestMissingDefinitionImport);
            RunCase("重复 ActorId / SkillId 导入失败", TestInvalidImportRejected);
            RunCase("0 经验不作为存档事实导出", TestZeroExpNotExported);
            RunCase("导入 0 经验后再次导出为空", TestImportZeroExpThenExportEmpty);
            RunCase("传承只复制允许继承的技艺", TestInheritance);
            RunCase("ViewData 排序稳定", TestViewDataSorting);
            RunCase("ViewData 字段计算正确", TestViewDataFields);
            RunCase("生命周期事件发布", TestLifecycleEvents);
            RunCase("UI ViewData 克隆稳定", TestViewDataClone);

            lastRunSucceeded = failedCheckCount == 0;
            lastReport = string.Join(Environment.NewLine, _reportLines);

            var summary = $"[NiumaGrowthTest] 基础测试结束：Passed={passedCheckCount}, Failed={failedCheckCount}";
            if (lastRunSucceeded)
            {
                UnityEngine.Debug.Log(summary, this);
            }
            else
            {
                UnityEngine.Debug.LogError(summary + Environment.NewLine + lastReport, this);
            }

            ReleaseCreatedAssets();
        }

        /// <summary>
        /// 清空最近一次测试报告。
        /// </summary>
        [ContextMenu("NiumaGrowthTest/清空测试报告")]
        public void ClearReport()
        {
            lastRunSucceeded = false;
            passedCheckCount = 0;
            failedCheckCount = 0;
            lastReport = string.Empty;
            _reportLines.Clear();
        }

        private void TestAddExpAndLevel()
        {
            var service = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100, 300));
            var result = service.AddExp(new GrowthExpRequest
            {
                ActorId = ActorId,
                SkillId = CraftSkillId,
                Amount = 120,
                SourceModule = nameof(GrowthBasicTestRunner)
            });

            ExpectSuccess("增加经验成功", result);
            ExpectEqual(120, service.GetTotalExp(ActorId, CraftSkillId), "总经验为 120");
            ExpectEqual(1, service.GetLevel(ActorId, CraftSkillId), "120 经验达到 1 级");
            Expect(service.MeetsRequirement(ActorId, new GrowthRequirementData { SkillId = CraftSkillId, RequiredLevel = 1 }), "满足 1 级需求");
            Expect(!service.MeetsRequirement(ActorId, new GrowthRequirementData { SkillId = CraftSkillId, RequiredLevel = 2 }), "不满足 2 级需求");
        }

        private void TestThresholdRecalculateAfterImport()
        {
            var oldService = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100, 300));
            ExpectSuccess("旧配置下增加经验", oldService.AddExp(new GrowthExpRequest
            {
                ActorId = ActorId,
                SkillId = CraftSkillId,
                Amount = 120,
                SourceModule = nameof(GrowthBasicTestRunner)
            }));

            var snapshot = oldService.ExportSnapshot();
            var newService = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 50, 100, 200));
            ExpectSuccess("新配置导入旧快照", newService.ImportSnapshot(snapshot));
            ExpectEqual(2, newService.GetLevel(ActorId, CraftSkillId), "阈值调整后等级按 TotalExp 重新计算为 2");
        }

        private void TestSetExp()
        {
            var service = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100, 300));
            var result = service.SetExp(ActorId, CraftSkillId, 320, nameof(GrowthBasicTestRunner));

            ExpectSuccess("SetExp 设置经验成功", result);
            ExpectEqual(320, service.GetTotalExp(ActorId, CraftSkillId), "SetExp 后 TotalExp 为 320");
            ExpectEqual(2, service.GetLevel(ActorId, CraftSkillId), "SetExp 后等级为 2");
            Expect(service.Revision > 0, "SetExp 修改数据后递增 Revision");
        }

        private void TestExportImportRoundTrip()
        {
            var source = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100, 300, 700));
            ExpectSuccess("导出前设置经验", source.SetExp(ActorId, CraftSkillId, 350, nameof(GrowthBasicTestRunner)));

            var expectedExp = source.GetTotalExp(ActorId, CraftSkillId);
            var expectedLevel = source.GetLevel(ActorId, CraftSkillId);
            var snapshot = source.ExportSnapshot();

            var restored = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100, 300, 700));
            ExpectSuccess("导入成长快照成功", restored.ImportSnapshot(snapshot));

            ExpectEqual(expectedExp, restored.GetTotalExp(ActorId, CraftSkillId), "导入后 TotalExp 保持一致");
            ExpectEqual(expectedLevel, restored.GetLevel(ActorId, CraftSkillId), "导入后 Level 保持一致");
            ExpectEqual(snapshot.Revision, restored.Revision, "导入后 Revision 继承存档");
        }

        private void TestMissingDefinitionImport()
        {
            var service = CreateService();
            var snapshot = new GrowthSaveData
            {
                Version = 1,
                Revision = 7,
                Owners = new[]
                {
                    new GrowthOwnerSnapshot
                    {
                        ActorId = ActorId,
                        Skills = new[]
                        {
                            new GrowthProgressSnapshot { SkillId = "missing_skill", TotalExp = 10 }
                        }
                    }
                }
            };

            ExpectSuccess("缺失配置快照仍可导入", service.ImportSnapshot(snapshot));
            var progress = service.GetProgress(ActorId, "missing_skill");
            Expect(progress != null, "缺失配置仍能构建 ViewData");
            Expect(progress.IsMissingDefinition, "缺失配置被标记 IsMissingDefinition");
            ExpectEqual(7L, service.Revision, "导入后 Revision 继承存档");
        }

        private void TestInvalidImportRejected()
        {
            var service = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100));
            var duplicateActor = new GrowthSaveData
            {
                Version = 1,
                Revision = 1,
                Owners = new[]
                {
                    CreateOwnerSnapshot(ActorId, CraftSkillId, 1),
                    CreateOwnerSnapshot(ActorId, CombatSkillId, 1)
                }
            };
            ExpectFailure("重复 ActorId 导入失败", service.ImportSnapshot(duplicateActor), GrowthFailureReason.ImportInvalid);

            var duplicateSkill = new GrowthSaveData
            {
                Version = 1,
                Revision = 1,
                Owners = new[]
                {
                    new GrowthOwnerSnapshot
                    {
                        ActorId = ActorId,
                        Skills = new[]
                        {
                            new GrowthProgressSnapshot { SkillId = CraftSkillId, TotalExp = 1 },
                            new GrowthProgressSnapshot { SkillId = CraftSkillId, TotalExp = 2 }
                        }
                    }
                }
            };
            ExpectFailure("重复 SkillId 导入失败", service.ImportSnapshot(duplicateSkill), GrowthFailureReason.ImportInvalid);

            var emptySkills = new GrowthSaveData
            {
                Version = 1,
                Revision = 1,
                Owners = new[] { new GrowthOwnerSnapshot { ActorId = ActorId, Skills = Array.Empty<GrowthProgressSnapshot>() } }
            };
            ExpectFailure("空 Skills 导入失败", service.ImportSnapshot(emptySkills), GrowthFailureReason.ImportInvalid);
        }

        private void TestZeroExpNotExported()
        {
            var service = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100));
            ExpectSuccess("显式设置 0 经验", service.SetExp(ActorId, CraftSkillId, 0, nameof(GrowthBasicTestRunner)));
            var snapshot = service.ExportSnapshot();
            ExpectEqual(0, snapshot.Owners.Length, "TotalExp=0 不导出为存档事实");
        }

        private void TestImportZeroExpThenExportEmpty()
        {
            var service = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100));
            var snapshot = new GrowthSaveData
            {
                Version = 1,
                Revision = 5,
                Owners = new[]
                {
                    CreateOwnerSnapshot(ActorId, CraftSkillId, 0)
                }
            };

            ExpectSuccess("导入 0 经验快照成功", service.ImportSnapshot(snapshot));
            ExpectEqual(0, service.GetTotalExp(ActorId, CraftSkillId), "导入后 TotalExp 为 0");
            ExpectEqual(0, service.GetLevel(ActorId, CraftSkillId), "导入后 Level 为 0");
            ExpectEqual(0, service.ExportSnapshot().Owners.Length, "导入 0 经验后再次导出为空");
        }

        private void TestInheritance()
        {
            var service = CreateService(
                CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100),
                CreateDefinition(CombatSkillId, GrowthCategory.Combat, false, 100));

            ExpectSuccess("来源增加可继承技艺经验", service.AddExp(new GrowthExpRequest { ActorId = ActorId, SkillId = CraftSkillId, Amount = 200, SourceModule = "test" }));
            ExpectSuccess("来源增加不可继承技艺经验", service.AddExp(new GrowthExpRequest { ActorId = ActorId, SkillId = CombatSkillId, Amount = 300, SourceModule = "test" }));

            var result = service.ApplyInheritance(new GrowthInheritanceRequest
            {
                SourceActorId = ActorId,
                TargetActorId = TargetActorId,
                ExpMultiplier = 0.5f,
                SourceModule = nameof(GrowthBasicTestRunner)
            });

            ExpectSuccess("传承请求成功", result);
            ExpectEqual(100, service.GetTotalExp(TargetActorId, CraftSkillId), "可继承技艺按倍率复制经验");
            ExpectEqual(0, service.GetTotalExp(TargetActorId, CombatSkillId), "不可继承技艺不会复制经验");
        }

        private void TestViewDataSorting()
        {
            var service = CreateService(
                CreateDefinition(CombatSkillId, GrowthCategory.Combat, true, 100),
                CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100),
                CreateDefinition(ScholarshipSkillId, GrowthCategory.Scholarship, true, 100));

            var all = service.GetAllProgress(ActorId, true);
            ExpectEqual(3, all.Length, "包含未开始技艺时返回全部配置");
            ExpectEqual(CraftSkillId, all[0].SkillId, "按 GrowthCategory 排序：Craft 在 Combat 前");
            ExpectEqual(CombatSkillId, all[1].SkillId, "Combat 排在第二");
            ExpectEqual(ScholarshipSkillId, all[2].SkillId, "Scholarship 排在第三");
        }

        private void TestViewDataFields()
        {
            var service = CreateService(CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100, 300));
            ExpectSuccess("设置经验用于 ViewData 字段测试", service.SetExp(ActorId, CraftSkillId, 160, "test"));

            var progress = service.GetProgress(ActorId, CraftSkillId);
            Expect(progress != null, "可以获取 GrowthProgressViewData");
            ExpectEqual(1, progress.Level, "ViewData.Level 正确");
            ExpectEqual(160, progress.TotalExp, "ViewData.TotalExp 正确");
            ExpectEqual(100, progress.CurrentLevelExp, "ViewData.CurrentLevelExp 正确");
            ExpectEqual(300, progress.NextLevelExp, "ViewData.NextLevelExp 正确");
            ExpectEqual(60, progress.ExpInCurrentLevel, "ViewData.ExpInCurrentLevel 正确");
            ExpectEqual(200, progress.ExpToNextLevel, "ViewData.ExpToNextLevel 正确");
            Expect(progress.Progress01 > 0f && progress.Progress01 < 1f, "ViewData.Progress01 在 0 到 1 之间");
            Expect(!progress.IsMaxLevel, "未满级时 IsMaxLevel 为 false");

            ExpectSuccess("设置到满级经验", service.SetExp(ActorId, CraftSkillId, 300, "test"));
            var maxProgress = service.GetProgress(ActorId, CraftSkillId);
            Expect(maxProgress != null && maxProgress.IsMaxLevel, "满级时 IsMaxLevel 为 true");
            ExpectEqual(-1, maxProgress.NextLevelExp, "满级时 NextLevelExp 为 -1");
        }

        private void TestLifecycleEvents()
        {
            var eventBus = new FakeEventBus();
            var service = CreateService(eventBus, CreateDefinition(CraftSkillId, GrowthCategory.Craft, true, 100, 300));

            ExpectSuccess("增加经验触发事件", service.AddExp(new GrowthExpRequest
            {
                ActorId = ActorId,
                SkillId = CraftSkillId,
                Amount = 120,
                SourceModule = "test"
            }));

            ExpectEqual(2, eventBus.DeferredEvents.Count, "升级时发布经验变化和等级变化两个事件");
            Expect(eventBus.DeferredEvents[0] is GrowthExpChangedEvent, "第一个事件为 GrowthExpChangedEvent");
            Expect(eventBus.DeferredEvents[1] is GrowthLevelChangedEvent, "第二个事件为 GrowthLevelChangedEvent");

            eventBus.DeferredEvents.Clear();
            ExpectSuccess("重置触发事件", service.ResetProgress(ActorId, CraftSkillId, "test"));
            ExpectEqual(1, eventBus.DeferredEvents.Count, "重置发布一个事件");
            Expect(eventBus.DeferredEvents[0] is GrowthProgressResetEvent, "重置事件类型正确");
        }

        private void TestViewDataClone()
        {
            var viewData = new GrowthPanelViewData
            {
                ActorId = ActorId,
                Revision = 3,
                Skills = new[]
                {
                    new GrowthProgressViewData { SkillId = CraftSkillId, TotalExp = 10, Level = 1 }
                }
            };

            var clone = viewData.Clone();
            clone.Skills[0].TotalExp = 99;

            ExpectEqual(10, viewData.Skills[0].TotalExp, "GrowthPanelViewData.Clone 会深拷贝 Skills");
        }

        private GrowthService CreateService(params GrowthSkillDefinition[] definitions)
        {
            return new GrowthService(definitions);
        }

        private GrowthService CreateService(IEventBus eventBus, params GrowthSkillDefinition[] definitions)
        {
            return new GrowthService(definitions, eventBus);
        }

        private GrowthSkillDefinition CreateDefinition(string skillId, GrowthCategory category, bool inheritable, params int[] thresholds)
        {
            var definition = ScriptableObject.CreateInstance<GrowthSkillDefinition>();
            definition.SkillId = skillId;
            definition.DisplayName = skillId;
            definition.Category = category;
            definition.IsInheritable = inheritable;
            definition.LevelThresholds = thresholds ?? Array.Empty<int>();
            _createdAssets.Add(definition);
            return definition;
        }

        private static GrowthOwnerSnapshot CreateOwnerSnapshot(string actorId, string skillId, int totalExp)
        {
            return new GrowthOwnerSnapshot
            {
                ActorId = actorId,
                Skills = new[]
                {
                    new GrowthProgressSnapshot { SkillId = skillId, TotalExp = totalExp }
                }
            };
        }

        private void RunCase(string caseName, Action test)
        {
            try
            {
                test();
                AddPass($"用例通过：{caseName}");
            }
            catch (Exception exception)
            {
                AddFail($"用例失败：{caseName} -> {exception.Message}");
            }
        }

        private void Expect(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }

            AddPass(message);
        }

        private void ExpectSuccess(string message, GrowthOperationResult result)
        {
            Expect(result != null && result.Succeeded, $"{message}：{result?.FailureReason} {result?.Message}");
        }

        private void ExpectFailure(string message, GrowthOperationResult result, GrowthFailureReason reason)
        {
            Expect(result != null && !result.Succeeded && result.FailureReason == reason, $"{message}：期望 {reason}，实际 {result?.FailureReason}");
        }

        private void ExpectEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"{message}：期望 {expected}，实际 {actual}");
            }

            AddPass(message);
        }

        private void AddPass(string message)
        {
            passedCheckCount++;
            if (verboseLog)
            {
                _reportLines.Add("[PASS] " + message);
            }
        }

        private void AddFail(string message)
        {
            failedCheckCount++;
            _reportLines.Add("[FAIL] " + message);
        }

        private void ResetReport()
        {
            ClearReport();
            ReleaseCreatedAssets();
        }

        private void ReleaseCreatedAssets()
        {
            for (var i = 0; i < _createdAssets.Count; i++)
            {
                var asset = _createdAssets[i];
                if (asset != null)
                {
                    DestroyImmediate(asset);
                }
            }

            _createdAssets.Clear();
        }

        private sealed class FakeEventBus : IEventBus
        {
            public readonly List<object> DeferredEvents = new List<object>();

            public void Publish<T>(T evt)
            {
            }

            public void Publish<T>(T evt, EventChannel channel)
            {
                if (channel == EventChannel.Deferred)
                {
                    DeferredEvents.Add(evt);
                }
            }

            public void Subscribe<T>(Action<T> handler)
            {
            }

            public void Unsubscribe<T>(Action<T> handler)
            {
            }

            public void DrainDeferred(int maxEvents = int.MaxValue)
            {
                // 模拟真实 EventBus：Drain 后延迟事件已被消费，测试若需验证事件内容应在 Drain 前读取。
                DeferredEvents.Clear();
            }
        }
    }
}
