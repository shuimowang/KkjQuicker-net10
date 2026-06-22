using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KkjQuicker.Domain.ClipboardHistory
{
    public abstract class ClipboardItem
    {
        /// <summary>唯一标识,仅用于按条目删除</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 条目类型标记,用于 JSON 反序列化判别子类型。
        /// 由各子类通过 override 返回固定字符串,外部不可修改。
        /// </summary>
        public abstract string ItemType { get; }

        /// <summary>记录写入时间</summary>
        public DateTime RecordTime { get; set; } = DateTime.Now;

        /// <summary>复制来源进程路径,用于按来源筛选</summary>
        public string CopiedFrom { get; set; }

        /// <summary>是否置顶,置顶项显示在剪贴板列表最前,并可在清空时跳过</summary>
        public bool IsPinned { get; set; }
    }

    public class TextClipboardItem : ClipboardItem
    {
        public override string ItemType => "Text";

        public string Text { get; set; }

        public string HtmlText { get; set; }

        /// <summary>
        /// 短标题。为空时界面可回退显示正文预览。
        /// 常用于常用项里给长文本、Token、Key、模板文本起别名。
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 界面显示标题，不持久化。
        /// </summary>
        [JsonIgnore]
        public string DisplayTitle
        {
            get
            {
                return string.IsNullOrWhiteSpace(Title) ? Text : Title;
            }
        }

        /// <summary>
        /// 内容图标,例如颜色文本显示颜色块,网址文本显示 favicon。
        /// 来源程序请使用基类的 CopiedFrom。
        /// </summary>
        public string Icon { get; set; }
    }

    public class ImageClipboardItem : ClipboardItem
    {
        public override string ItemType => "Image";

        public string ImagePath { get; set; }

        public int PixelWidth { get; set; }

        public int PixelHeight { get; set; }

        public string ImageHash { get; set; }
    }

    public class FileClipboardItem : ClipboardItem
    {
        public override string ItemType => "File";

        public string[] FilePaths { get; set; }
    }

    public class CustomFormatClipboardItem : ClipboardItem
    {
        public override string ItemType => "CustomFormat";

        /// <summary>剪贴板自定义格式,例如 quicker-action-steps。必须原样写回剪贴板。</summary>
        public string Format { get; set; }

        /// <summary>格式显示名称,例如 Quicker动作步骤。仅用于 UI 展示。</summary>
        public string FormatTitle { get; set; }

        /// <summary>格式说明,例如 从 Quicker 复制的动作步骤。仅用于 UI 展示。</summary>
        public string FormatDescription { get; set; }

        /// <summary>原始 JSON,用于原样写回剪贴板。</summary>
        public string RawJson { get; set; }

        /// <summary>展示列表,例如动作步骤列表。</summary>
        public List<CustomFormatDisplayItem> DisplayItems { get; set; }

        public CustomFormatClipboardItem()
        {
            DisplayItems = new List<CustomFormatDisplayItem>();
        }

        public void EnsureDisplayItems()
        {
            if (DisplayItems == null)
                DisplayItems = new List<CustomFormatDisplayItem>();
        }
    }

    public class CustomFormatDisplayItem
    {
        /// <summary>图标,例如 fa:Solid_Bolt</summary>
        public string Icon { get; set; }

        /// <summary>主标题,例如步骤名称</summary>
        public string Title { get; set; }

        /// <summary>说明</summary>
        public string Description { get; set; }
    }

    public class ClipboardTemplateSelector : DataTemplateSelector
    {
        public DataTemplate TextTemplate { get; set; }

        public DataTemplate ImageTemplate { get; set; }

        public DataTemplate FileTemplate { get; set; }

        public DataTemplate CustomFormatTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is TextClipboardItem)
                return TextTemplate;

            if (item is ImageClipboardItem)
                return ImageTemplate;

            if (item is FileClipboardItem)
                return FileTemplate;

            if (item is CustomFormatClipboardItem)
                return CustomFormatTemplate;

            return base.SelectTemplate(item, container);
        }
    }

    /// <summary>
    /// 剪贴板历史 JSON 读写辅助类。
    /// 统一处理 ClipboardItem 子类型反序列化、损坏数据兜底、null 项过滤。
    /// </summary>
    public static class ClipboardHistoryJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new ClipboardItemConverter() }
        };

        public static List<ClipboardItem> ReadItems(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<ClipboardItem>();

            try
            {
                var items = JsonConvert.DeserializeObject<List<ClipboardItem>>(json, Settings);

                if (items == null)
                    return new List<ClipboardItem>();

                return items
                    .Where(x => x != null)
                    .ToList();
            }
            catch
            {
                return new List<ClipboardItem>();
            }
        }

        public static ObservableCollection<ClipboardItem> ReadItemsCollection(string json)
        {
            return new ObservableCollection<ClipboardItem>(ReadItems(json));
        }

        public static string WriteItems(IEnumerable<ClipboardItem> items)
        {
            var safeItems = items == null
                ? Enumerable.Empty<ClipboardItem>()
                : items.Where(x => x != null);

            return JsonConvert.SerializeObject(
                safeItems,
                Formatting.None,
                Settings);
        }
    }

    /// <summary>
    /// 通过 ClipboardItem.ItemType 字段推断子类型的 JSON 转换器。
    /// 仅自定义反序列化,序列化使用默认行为。
    /// </summary>
    public class ClipboardItemConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(ClipboardItem).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            JObject jo = JObject.Load(reader);
            string itemType = (string)jo["ItemType"];

            ClipboardItem item;

            switch (itemType)
            {
                case "Text":
                    item = new TextClipboardItem();
                    break;

                case "Image":
                    item = new ImageClipboardItem();
                    break;

                case "File":
                    item = new FileClipboardItem();
                    break;

                case "CustomFormat":
                    item = new CustomFormatClipboardItem();
                    break;

                default:
                    return null;
            }

            serializer.Populate(jo.CreateReader(), item);

            var customFormatItem = item as CustomFormatClipboardItem;
            if (customFormatItem != null)
                customFormatItem.EnsureDisplayItems();

            return item;
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
        }
    }
}