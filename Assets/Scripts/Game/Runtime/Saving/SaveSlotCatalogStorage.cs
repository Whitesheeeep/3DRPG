using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using RPG.SaveSystem;
using WS_Modules.LogModule;

namespace RPG.Game.Saving
{
    /// <summary>持久保存十个卡位到 GUID 的稳定关系，目录索引不保存游戏数据。</summary>
    internal sealed class SaveSlotCatalogStorage
    {
        #region 依赖字段
        private readonly string catalogPath;
        #endregion

        #region 索引存取
        /// <summary>绑定游戏存档目录。</summary>
        /// <param name="directory">存档目录。</param>
        public SaveSlotCatalogStorage(string directory) => catalogPath = Path.Combine(directory, "slot-catalog.json");

        /// <summary>读取已存在索引，首次建立时接纳现有文件并填满十个固定 ID。</summary>
        /// <param name="details">已枚举的存档文件。</param>
        /// <returns>十个不重复的稳定 ID。</returns>
        public IReadOnlyList<SaveSlotId> LoadOrCreate(IReadOnlyList<SaveSlotDetails> details)
        {
            List<string> values;
            if (File.Exists(catalogPath))
            {
                values = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(catalogPath));
                if (values == null || values.Count != 10)
                    throw new InvalidDataException("[GameSaveSystem] 卡位目录损坏，需要保留文件后修复目录索引。");
            }
            else
            {
                if (details.Count > 10) throw new InvalidDataException("[GameSaveSystem] 已有存档超过十个，不能自动分配卡位。");
                values = new List<string>(10);
                foreach (SaveSlotDetails detail in details) values.Add(detail.Entry.SlotId.Value);
                while (values.Count < 10) values.Add(SaveSlotId.CreateNew().Value);
                Directory.CreateDirectory(Path.GetDirectoryName(catalogPath));
                string temporaryPath = catalogPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(values, Formatting.Indented));
                // 正式索引只在临时文件完整写入后出现；目录创建过程中不覆盖任何游戏存档。
                File.Move(temporaryPath, catalogPath);
                WSLog.Log("[GameSaveSystem] 已创建十个固定存档卡位。");
            }
            var result = new List<SaveSlotId>(10);
            var seen = new HashSet<SaveSlotId>();
            foreach (string value in values)
            {
                if (!SaveSlotId.TryParse(value, out SaveSlotId slotId) || !seen.Add(slotId))
                    throw new InvalidDataException("[GameSaveSystem] 卡位索引包含无效或重复的槽位 ID。");
                result.Add(slotId);
            }
            foreach (SaveSlotDetails detail in details)
                if (!seen.Contains(detail.Entry.SlotId))
                    throw new InvalidDataException("[GameSaveSystem] 发现尚未分配卡位的存档，保留原文件并停止自动选档。");
            return result.AsReadOnly();
        }
        #endregion
    }
}
