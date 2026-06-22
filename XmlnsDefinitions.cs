using System.Windows.Markup;

// UI 根命名空间：xmlns:kk="https://kkjquicker.net/xaml"
[assembly: XmlnsPrefix("https://kkjquicker.net/xaml", "kk")]

// 常用 UI 子命名空间
[assembly: XmlnsDefinition("https://kkjquicker.net/xaml", "KkjQuicker.UI.Behaviors")]
[assembly: XmlnsDefinition("https://kkjquicker.net/xaml", "KkjQuicker.UI.Converters")]
[assembly: XmlnsDefinition("https://kkjquicker.net/xaml", "KkjQuicker.UI.Controls")]
[assembly: XmlnsDefinition("https://kkjquicker.net/xaml", "KkjQuicker.Domain.ClipboardHistory")]