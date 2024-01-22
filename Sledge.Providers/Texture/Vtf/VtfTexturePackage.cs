using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Sledge.FileSystem;
using Sledge.Formats.Texture.Vtf;
using Sledge.Packages.Vpk;
using Sledge.Providers.Texture.Wad;
using Sledge.Providers.Texture.Wad.Format;

namespace Sledge.Providers.Texture.Vtf
{
    public class VtfTexturePackage : TexturePackage
    {
        private IFile _file;
        private Dictionary<string, string> _displayName = new Dictionary<string, string>();

        protected override IEqualityComparer<string> GetComparer => StringComparer.InvariantCultureIgnoreCase;

        public VtfTexturePackage(InlinePackageFile vpk, TexturePackageReference[] references) : base(Path.GetFileName(vpk.FullPathName), "Vtf")
        {
            _file = vpk;
            List<string> textureList = new List<string>();
            foreach (var reference in references)
            {
                _displayName.Add(reference.Name, reference.Name.Substring(reference.Name.LastIndexOf("/") + 1, reference.Name.LastIndexOf(".") - 1 - reference.Name.LastIndexOf("/")));
                textureList.Add(reference.Name.Substring(reference.Name.IndexOf("/") + 1, reference.Name.LastIndexOf(".") - 1 - reference.Name.IndexOf("/")));
            }
            /*if (textureList.Count > 0) {
                for (int i = 0; i < 50; i++)
                {
                    string testreferencename;
                    _displayName.TryGetValue(references[i].Name, out testreferencename);
                    Debug.WriteLine(testreferencename);
                    Debug.WriteLine(references[i].Name + " ("+ references[i].Name.Substring(references[i].Name.IndexOf("/") + 1, references[i].Name.LastIndexOf(".") - 1 - references[i].Name.IndexOf("/")) + ") = " + testreferencename);
                }
            }*/

            Textures.UnionWith(textureList);
        }

        private TextureFlags GetFlags(WadEntry entry)
        {
            return _file.NameWithoutExtension.IndexOf("decal", StringComparison.CurrentCultureIgnoreCase) >= 0 && entry.Name.StartsWith("{")
                ? TextureFlags.Transparent
                : TextureFlags.None;
        }

        public override async Task<IEnumerable<TextureItem>> GetTextures(IEnumerable<string> names)
        {
            var textures = new HashSet<string>(names.Select(name => "materials/" + name + ".vtf"));
            textures.IntersectWith(Textures);
            if (!textures.Any()) return new TextureItem[0];

            var vpkdir = new VpkDirectory(new FileInfo(_file.FullPathName));
            var list = new List<TextureItem>();

            foreach (var name in textures)
            {
                Stream fileStream;
                try {
                    fileStream = vpkdir.OpenFile("materials/"+name+".vtf");
                } catch (Exception e) { continue; }

                var wp = new VtfFile(fileStream);
                fileStream.Close();

                VtfImage entry = null;//wp.LowResImage;
                if (entry == null) entry = wp.Images.Last();
                if (entry == null) continue;
                var item = new TextureItem(name, TextureFlags.None, (int)entry.Width, (int)entry.Height);
                list.Add(item);
            }

            return list;
        }

        public override string GetDisplayName(TexturePackageReference reference)
        {
            string displayName;
            if (_displayName.TryGetValue(reference.Name, out displayName))
                return displayName;
            return reference.Name;
        }

        public override string GetPathFromDisplay(string name)
        {
            foreach (KeyValuePair<string, string> entry in _displayName) {
                if (entry.Value == name)
                    return entry.Key;
            }
            return name;
        }

        public override async Task<TextureItem> GetTexture(string name)
        {
            if (!Textures.Contains(name)) return null;

            var vpkdir = new VpkDirectory(new FileInfo(_file.FullPathName));
            var files = vpkdir.GetFiles();
            foreach (string file in files)
            {
                if (file != "materials/" + name + ".vtf") continue;

                var vtfreader = vpkdir.OpenFile("materials/" + name + ".vtf");
                var wp = new VtfFile(vtfreader);
                VtfImage entry = null;// wp.LowResImage;
                if (entry == null) entry = wp.Images.Last();
                if (entry == null) continue;
                vtfreader.Close();
                return new TextureItem(name, TextureFlags.None/*GetFlags(entry)*/, (int)entry.Width, (int)entry.Height, Path.GetFileName(_file.FullPathName));
            }
            return null;
        }

        public override ITextureStreamSource GetStreamSource()
        {
            return new VtfStreamSource(_file);
        }
    }
}