using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 存储解析后的窗口组件数据，包括字段名称、字段类型、实例ID和列表元素数据（如果是列表类型）
    /// </summary>
    public class EditorObjectData
    {
        public int insID;
        public string fieldName;
        public string fieldType;
        public List<EditorObjectData> dataList;
    }
    
    public static class AnalysisComponentDataTool
    {
        /// <summary>
        /// 解析窗口节点数据，将解析后的数据存储在objDataList中，供后续生成代码使用
        /// </summary>
        // 如果节点名称包含#，则认为该节点不需要绑定，直接跳过，如果节点名称包含[]，则认为该节点需要绑定，并且[]中包含字段类型和字段昵称，字段类型和字段昵称之间用]分隔
        // 如果字段类型包含逗号，说明该字段是一个列表类型，需要将列表元素的数据存储在dataList中，供后续生成代码使用
        // 对于数组，不论子对象中名字是否包含#，都需要进行绑定，因为数组元素的名字不参与绑定，绑定时只根据父对象的字段类型进行绑定即可
        public static void  AnalysisWindowNodeData(ref List<EditorObjectData> objDataList, Transform trans, string WinName)
        {
            for (int i = 0; i < trans.childCount; i++)
            {
                GameObject obj = trans.GetChild(i).gameObject;
                string name = obj.name;
            
                if (name.Contains("#"))    continue;
            
                if (name.Contains("[") && name.Contains("]"))
                {
                    int index = name.IndexOf("]", StringComparison.Ordinal) + 1;
                    string fieldName = name.Substring(index, name.Length - index);//获取字段昵称
                    fieldName = System.Text.RegularExpressions.Regex.Replace(fieldName.Trim(), @"\p{C}", "");
                    string fieldType = name.Substring(1, index - 2);//获取字段类型
                    var objectData = new EditorObjectData { fieldName = fieldName, fieldType = fieldType, insID = obj.GetInstanceID() };
                    objDataList.Add(objectData);
                    //处理列表元素绑定
                    if (fieldType.Contains(","))
                    {
                        objectData.dataList = new List<EditorObjectData>();
                        objectData.fieldType = objectData.fieldType.Replace(",", "");
                        for (int j = 0; j < obj.transform.childCount; j++) 
                        {
                            GameObject listObjItme = obj.transform.GetChild(j).gameObject;
                            objectData.dataList.Add(new EditorObjectData { fieldName = listObjItme.name.Replace("#",""),  insID = listObjItme.GetInstanceID()});
                        }
                    }
                }
                AnalysisWindowNodeData(ref objDataList,trans.GetChild(i), WinName);
            }
        }

        /// <summary>
        /// 解析窗口Tag数据
        /// </summary>
        /// <param name="objDataList"></param>
        /// <param name="trans"></param>
        /// <param name="WinName"></param>
        public static void AnalysisWindowDataByTag(ref List<EditorObjectData> objDataList,Transform trans, string WinName)
        {
            for (int i = 0; i < trans.childCount; i++)
            {
                GameObject obj = trans.GetChild(i).gameObject;
            
                if (obj.name.Contains("#"))    continue;
            
                string tagName = obj.tag;
            
                if (GeneratorConfig.TAGArr.Contains(tagName))
                {
                    string fieldName = obj.name;//获取字段昵称
                    string fieldType = tagName;//获取字段类型
                    objDataList.Add(new EditorObjectData { fieldName = fieldName, fieldType = fieldType, insID = obj.GetInstanceID() });
                }
                AnalysisWindowDataByTag(ref objDataList, trans.GetChild(i), WinName);
            }
        }
        /// <summary>
        /// 判断对象是否是预制体
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static bool IsPrefabInstance(GameObject obj)
        {
            var type = PrefabUtility.GetPrefabAssetType(obj);
            var status= PrefabUtility.GetPrefabInstanceStatus(obj);
            //是否是预制体实例
            return status != PrefabInstanceStatus.NotAPrefab && type != PrefabAssetType.NotAPrefab;
        }
    }
}