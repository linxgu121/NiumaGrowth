using System;
using System.Collections.Generic;
using System.Text;
using NiumaGrowth.Controller;
using NiumaGrowth.Data;
using NiumaSave.Controller;
using NiumaSave.Data;
using NiumaSave.Provider;
using UnityEngine;

namespace NiumaGrowth.SaveBridge
{
    /// <summary>
    /// NiumaGrowth 存档桥接器。
    /// 负责把成长快照转换为 NiumaSave Section，并在读档时恢复到成长控制器。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NiumaGrowthSaveAdapter : MonoBehaviour, ISaveDataProvider
    {
        private const string GrowthSectionId = "growth";
        private const string GrowthSectionVersionV1 = "1";
        private const string CurrentGrowthSectionVersion = GrowthSectionVersionV1;
        private const string GrowthSectionFormat = "json";

        [Header("模块引用")]
        [Tooltip("成长模块根控制器。请拖入场景中的 NiumaGrowthController，导出和导入成长状态都会通过它完成。")]
        [SerializeField] private NiumaGrowthController growthController;

        [Tooltip("存档模块根控制器。开启自动注册时，请拖入场景中的 NiumaSaveController。")]
        [SerializeField] private NiumaSaveController saveController;

        [Header("注册行为")]
        [Tooltip("启用组件时是否自动注册到 NiumaSaveController。正式场景建议开启，并确保 NiumaSaveController 更早初始化。")]
        [SerializeField] private bool registerOnEnable = true;

        [Tooltip("引用为空时是否自动在场景中查找对应组件。仅建议调试阶段开启；正式多场景或全局场景必须手动绑定，避免找到错误实例。")]
        [SerializeField] private bool autoFindReferences = true;

        private bool _registeredToSaveController;

        public string SectionId => GrowthSectionId;
        public string SectionVersion => CurrentGrowthSectionVersion;
        public long Revision => growthController != null ? growthController.GrowthRevision : 0L;

        private void Awake()
        {
            ResolveReferences(false);
        }

        private void OnEnable()
        {
            if (registerOnEnable)
            {
                RegisterToSaveController();
            }
        }

        private void OnDisable()
        {
            UnregisterFromSaveController();
        }

        /// <summary>
        /// 导出成长快照为 NiumaSave Section。
        /// SaveDataProviderRegistry 会捕获该方法抛出的异常并转为结构化导出失败；直接调用时必须自行处理 InvalidOperationException。
        /// </summary>
        public SaveSectionData ExportSection()
        {
            ResolveReferences(false);
            if (growthController == null)
            {
                throw new InvalidOperationException("NiumaGrowthSaveAdapter 缺少 NiumaGrowthController，无法导出成长存档。");
            }

            if (!growthController.IsInitialized)
            {
                throw new InvalidOperationException("NiumaGrowthController 尚未初始化，拒绝导出空成长存档以避免覆盖有效数据。");
            }

            var saveData = growthController.ExportSnapshot();
            ValidateSaveDataForExport(saveData);

            var json = JsonUtility.ToJson(saveData);
            var bytes = Encoding.UTF8.GetBytes(json);

            return new SaveSectionData
            {
                SectionId = SectionId,
                SectionVersion = SectionVersion,
                Format = GrowthSectionFormat,
                DataEncoding = SaveDataEncoding.Base64,
                EncodedData = Convert.ToBase64String(bytes)
            };
        }

        /// <summary>
        /// 从 NiumaSave Section 导入成长快照。
        /// 导入前会先完成结构校验；损坏或空数据不会清空当前运行中的成长进度。
        /// </summary>
        public SaveSectionImportResult ImportSection(SaveSectionData section)
        {
            ResolveReferences(false);
            if (growthController == null)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.ConfigMissing,
                    "NiumaGrowthSaveAdapter 缺少 NiumaGrowthController，无法导入成长存档。");
            }

            if (section == null)
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.NullSection, "成长存档段为空。");
            }

            if (!string.Equals(section.SectionId, SectionId, StringComparison.Ordinal))
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.SectionIdMismatch,
                    $"成长存档段 ID 不匹配：expected={SectionId}, actual={section.SectionId}");
            }

            if (!string.Equals(section.Format, GrowthSectionFormat, StringComparison.Ordinal))
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"成长存档段格式不支持：{section.Format}");
            }

            if (!string.Equals(section.DataEncoding, SaveDataEncoding.Base64, StringComparison.Ordinal))
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"成长存档段编码不支持：{section.DataEncoding}");
            }

            if (string.IsNullOrWhiteSpace(section.EncodedData))
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, "成长存档段数据为空。");
            }

            try
            {
                var readResult = TryReadGrowthSaveData(section, out var saveData);
                if (!readResult.Succeeded)
                {
                    return readResult;
                }

                var importResult = growthController.ImportSnapshot(saveData);
                if (importResult == null || !importResult.Succeeded)
                {
                    return SaveSectionImportResult.Fail(
                        SaveSectionImportErrorCode.ImportFailed,
                        importResult != null ? importResult.Message : "成长控制器导入结果为空。");
                }

                return SaveSectionImportResult.Success();
            }
            catch (Exception ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.Unknown,
                    $"成长存档段导入异常：{ex.Message}");
            }
        }

        private static SaveSectionImportResult TryReadGrowthSaveData(SaveSectionData section, out GrowthSaveData saveData)
        {
            saveData = null;
            switch (section.SectionVersion)
            {
                case GrowthSectionVersionV1:
                    return TryReadVersion1(section, out saveData);
                default:
                    return SaveSectionImportResult.Fail(
                        SaveSectionImportErrorCode.VersionUnsupported,
                        $"成长存档段版本不支持：{section.SectionVersion}");
            }
        }

        private static SaveSectionImportResult TryReadVersion1(SaveSectionData section, out GrowthSaveData saveData)
        {
            saveData = null;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(section.EncodedData);
            }
            catch (FormatException ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"成长存档段 Base64 解码失败：{ex.Message}");
            }

            string json;
            try
            {
                json = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"成长存档段 UTF8 解码失败：{ex.Message}");
            }

            try
            {
                saveData = JsonUtility.FromJson<GrowthSaveData>(json);
            }
            catch (ArgumentException ex)
            {
                return SaveSectionImportResult.Fail(
                    SaveSectionImportErrorCode.DataCorrupted,
                    $"成长存档段 Json 解析失败：{ex.Message}");
            }

            return ValidateImportedSaveData(saveData);
        }

        [ContextMenu("NiumaGrowthSave/注册到存档模块")]
        private void RegisterToSaveController()
        {
            if (_registeredToSaveController)
            {
                return;
            }

            ResolveReferences(true);
            if (saveController == null)
            {
                return;
            }

            var registered = saveController.RegisterProvider(this);
            _registeredToSaveController = registered;
            if (!registered)
            {
                Debug.LogWarning("[NiumaGrowthSaveAdapter] 注册成长存档 Provider 失败。", this);
            }
        }

        [ContextMenu("NiumaGrowthSave/从存档模块取消注册")]
        private void UnregisterFromSaveController()
        {
            ResolveReferences(false);
            if (!_registeredToSaveController || saveController == null)
            {
                _registeredToSaveController = false;
                return;
            }

            saveController.UnregisterProvider(SectionId);
            _registeredToSaveController = false;
        }

        private void ResolveReferences(bool logWarnings)
        {
            if (!autoFindReferences)
            {
                return;
            }

            if (growthController == null)
            {
#if UNITY_2023_1_OR_NEWER
                growthController = FindFirstObjectByType<NiumaGrowthController>();
#else
                growthController = FindObjectOfType<NiumaGrowthController>();
#endif
            }

            if (saveController == null)
            {
#if UNITY_2023_1_OR_NEWER
                saveController = FindFirstObjectByType<NiumaSaveController>();
#else
                saveController = FindObjectOfType<NiumaSaveController>();
#endif
            }

            if (logWarnings && growthController == null)
            {
                Debug.LogWarning("[NiumaGrowthSaveAdapter] 未找到 NiumaGrowthController。", this);
            }

            if (logWarnings && saveController == null)
            {
                Debug.LogWarning("[NiumaGrowthSaveAdapter] 未找到 NiumaSaveController。", this);
            }
        }

        private static void ValidateSaveDataForExport(GrowthSaveData saveData)
        {
            var result = ValidateImportedSaveData(saveData);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"成长存档导出数据无效：{result.Message}");
            }
        }

        private static SaveSectionImportResult ValidateImportedSaveData(GrowthSaveData saveData)
        {
            if (saveData == null)
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, "成长存档数据为空。");
            }

            if (saveData.Version != 1)
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.VersionUnsupported, $"成长存档内部版本不支持：{saveData.Version}");
            }

            if (saveData.Revision < 0L)
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, "成长存档 Revision 不能为负数。");
            }

            if (saveData.Owners == null)
            {
                return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, "成长存档 Owners 为空。");
            }

            var actorIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < saveData.Owners.Length; i++)
            {
                var owner = saveData.Owners[i];
                if (owner == null || string.IsNullOrWhiteSpace(owner.ActorId) || owner.Skills == null || owner.Skills.Length == 0)
                {
                    return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, $"成长存档 Owners[{i}] 数据无效。");
                }

                if (!actorIds.Add(owner.ActorId))
                {
                    return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, $"成长存档存在重复 ActorId：{owner.ActorId}");
                }

                var skillIds = new HashSet<string>(StringComparer.Ordinal);
                for (var j = 0; j < owner.Skills.Length; j++)
                {
                    var progress = owner.Skills[j];
                    if (progress == null || string.IsNullOrWhiteSpace(progress.SkillId))
                    {
                        return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, $"成长存档 Actor={owner.ActorId} 的 Skills[{j}] 数据无效。");
                    }

                    if (progress.TotalExp < 0)
                    {
                        return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, $"成长存档 Actor={owner.ActorId}, SkillId={progress.SkillId} 的 TotalExp 不能为负数。");
                    }

                    if (!skillIds.Add(progress.SkillId))
                    {
                        return SaveSectionImportResult.Fail(SaveSectionImportErrorCode.DataCorrupted, $"成长存档 Actor={owner.ActorId} 存在重复 SkillId：{progress.SkillId}");
                    }
                }
            }

            return SaveSectionImportResult.Success();
        }
    }
}
