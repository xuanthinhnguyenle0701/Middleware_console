using System.Collections.Generic;

namespace Middleware_console 
{
    public class ScadaScreenModel
    {
        public string ScreenName { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public List<ScadaLayerModel> Layers { get; set; }
        public List<ScadaItemModel> Items { get; set; }
    }

    public class ScadaLayerModel
    {
        public string LayerName { get; set; }
        public List<ScadaItemModel> Items { get; set; }
    }

    public class ScadaItemModel
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public bool? EnableCreation { get; set; } = true;
        public Dictionary<string, object> Properties { get; set; }
        public Dictionary<string, string> Events { get; set; }
        public List<ScadaItemModel> Items { get; set; }
        public string TagName { get; set; }
        public LibraryModel Library { get; set; }
    }

    public class LibraryModel
    {
        public string LibraryPath { get; set; }
        public string SubLibrary { get; set; }
    }
}