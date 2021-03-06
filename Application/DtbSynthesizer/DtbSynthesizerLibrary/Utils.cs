﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using DtbSynthesizerLibrary.Xml;
using Saxon.Api;

namespace DtbSynthesizerLibrary
{
    public static class Utils
    {
        public static XNamespace XhtmlNs => "http://www.w3.org/1999/xhtml";
        public static XNamespace OcfNs => "urn:oasis:names:tc:opendocument:xmlns:container";
        public static XNamespace OpfNs => "http://www.idpf.org/2007/opf";
        public static XNamespace DcNs => "http://purl.org/dc/elements/1.1/";

        /// <summary>
        /// Gets the language of an <see cref="XElement"/>
        /// from the xml:lang or lang <see cref="XAttribute"/>s.
        /// If both attributes have non-whitespace value, xml:lang takes precedent
        /// </summary>
        /// <param name="elem">The <see cref="XElement"/></param>
        /// <returns>The language or <c>null</c> is not present</returns>
        public static string GetLanguage(XElement elem)
        {
            if (elem == null)
            {
                return null;
            }
            var lang = elem.Attribute(XNamespace.Xml + "lang")?.Value;
            if (String.IsNullOrWhiteSpace(lang))
            {
                lang = elem.Attribute("lang")?.Value;
            }
            return String.IsNullOrWhiteSpace(lang) ? null : lang;
        }

        public static CultureInfo SelectCulture(XNode node)
        {
            var lang =
                GetLanguage(node as XElement)
                ?? GetLanguage(node
                    .Ancestors()
                    .FirstOrDefault(elem => GetLanguage(elem) != null));
            try
            {
                return lang == null ? CultureInfo.InvariantCulture : new CultureInfo(lang);

            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        public static IEnumerable<IXmlSynthesizer> GetAllSynthesizers()
        {
            return SystemSpeechXmlSynthesizer
                .Synthesizers
                .Concat(MicrosoftSpeechXmlSynthesizer.Synthesizers);
        }

        public static IXmlSynthesizer GetPrefferedXmlSynthesizerForCulture(CultureInfo ci,
            IXmlSynthesizer defaultSynthesizer = null)
        {
            return GetPrefferedXmlSynthesizerForCulture(ci, GetAllSynthesizers().ToList(), defaultSynthesizer);
        }

        public static IXmlSynthesizer GetPrefferedXmlSynthesizerForCulture(
            CultureInfo ci,
            IReadOnlyCollection<IXmlSynthesizer> synthesizerList,
            IXmlSynthesizer defaultSynthesizer = null)
        {
            defaultSynthesizer = defaultSynthesizer ?? synthesizerList.FirstOrDefault();
            if (CultureInfo.InvariantCulture.Equals(ci))
            {
                return defaultSynthesizer;
            }
            return
                synthesizerList.FirstOrDefault(s => s.VoiceInfo.Culture.Equals(ci))
                ?? synthesizerList.FirstOrDefault(s =>
                    s.VoiceInfo.Culture.TwoLetterISOLanguageName == ci.TwoLetterISOLanguageName)
                ?? defaultSynthesizer;
        }

        private static readonly Regex GeneratedIdRegex = new Regex("^IX\\d{5,}$");

        public static int SetMissingIds(XElement elem)
        {
            return elem
                .DescendantsAndSelf()
                .Where(e => String.IsNullOrEmpty(e.Attribute("id")?.Value))
                .Select(e =>
                {
                    e.SetAttributeValue("id", GenerateNewId(e.Document));
                    return 1;
                })
                .Sum();
        }

        public static string GenerateNewId(XDocument doc)
        {
            var ids = new HashSet<ulong>(doc
                .Descendants()
                .Select(elem => elem.Attribute("id")?.Value ?? "")
                .Distinct()
                .Where(id => GeneratedIdRegex.IsMatch(id))
                .Select(id => UInt64.Parse(id.Substring(2))));
            ulong nextId = 0;
            while (ids.Contains(nextId))
            {
                nextId++;
            }
            return $"IX{nextId:D5}";
        }

        /// <summary>
        /// Generates a skeleton xhtml <see cref="XDocument"/>
        /// </summary>
        /// <returns>The skeleton xhtml <see cref="XDocument"/></returns>
        public static XDocument GenerateSkeletonXhtmlDocument(string baseUri = null)
        {
            return CloneWithBaseUri(
                new XDocument(
                    new XDocumentType(
                        "html",
                        "-//W3C//DTD XHTML 1.0 Transitional//EN",
                        "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd",
                        null),
                    new XElement(
                        Utils.XhtmlNs + "html",
                        new XElement(
                            Utils.XhtmlNs + "head",
                            new XElement(
                                Utils.XhtmlNs + "meta",
                                new XAttribute("http-equiv", "Content-type"),
                                new XAttribute("content", "text/html; charset=utf-8"))),
                        new XElement(Utils.XhtmlNs + "body"))),
                baseUri);
        }

        /// <summary>
        /// Generates a skeleton SMIL 1.0 <see cref="XDocument"/> for a Daisy 2.02 DTB
        /// </summary>
        /// <returns>The skeleton SMIL 1.0 <see cref="XDocument"/></returns>
        public static XDocument GenerateSkeletonDaisy202SmilDocument(string baseUri = null)
        {
            return CloneWithBaseUri(
                new XDocument(
                    new XDocumentType(
                        "smil",
                        "-//W3C//DTD SMIL 1.0//EN",
                        "http://www.w3.org/TR/REC-SMIL/SMIL10.dtd",
                        null),
                    new XElement(
                        "smil",
                        new XElement(
                            "head",
                            new XElement(
                                "meta",
                                new XAttribute("name", "dc:format"),
                                new XAttribute("content", "Daisy 2.02")),
                            new XElement(
                                "layout",
                                new XElement("region", new XAttribute("id", "txtView")))),
                        new XElement(
                            "body",
                            new XElement("seq")))),
                baseUri);
        }

        public static XElement CloneWithBaseUri(XElement element, string baseUri = null)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            baseUri = baseUri ?? element.BaseUri;
            return XElement.Load(
                (baseUri == null)
                    ? element.CreateReader()
                    : XmlReader.Create(new StringReader(element.ToString()), new XmlReaderSettings(), baseUri),
                LoadOptions.SetBaseUri);
        }

        public static XDocument CloneWithBaseUri(XDocument document, string baseUri = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            baseUri = baseUri ?? document.BaseUri;
            var res = XDocument.Load(
                (baseUri == null)
                    ? document.CreateReader()
                    : XmlReader.Create(new StringReader(document.ToString()),
                        new XmlReaderSettings() {DtdProcessing = DtdProcessing.Parse}, baseUri),
                LoadOptions.SetBaseUri);
            if (res.DocumentType != null && String.IsNullOrWhiteSpace(res.DocumentType.InternalSubset))
            {
                res.DocumentType.InternalSubset = null;
            }
            return res;
        }

        /// <summary>
        /// Gets or creates a meta <see cref="XElement"/> in a <see cref="XDocument"/> with a given name.
        /// </summary>
        /// <param name="doc">The <see cref="XDocument"/></param>
        /// <param name="name">The name of the meta <see cref="XElement"/> (that is the value of the name <see cref="XAttribute"/></param>
        /// <param name="createIfMissing">Creates and adds the meta if missing</param>
        /// <returns>
        /// An existing or, if not already present, newly added meta <see cref="XElement"/> from the head section of <paramref name="doc"/>
        /// </returns>
        private static XElement GetMeta(XDocument doc, string name, bool createIfMissing = true)
        {
            var head = doc.Root?.Element(doc.Root.Name.Namespace + "head");
            if (head == null)
            {
                return null;
            }

            var meta = head.Elements(head.Name.Namespace + "meta")
                .FirstOrDefault(m => m.Attribute("name")?.Value == name);
            if (meta == null && createIfMissing)
            {
                meta = new XElement(head.Name.Namespace + "meta", new XAttribute("name", name));
                head.Add(meta);
            }

            return meta;
        }

        public static string GetMetaContent(XDocument doc, string name)
        {
            return GetMeta(doc, name, false)?.Attribute("content")?.Value;
        }

        public static XElement SetMeta(XDocument doc, string name, string content)
        {
            var meta = GetMeta(doc, name);
            meta?.SetAttributeValue("content", content);
            return meta;
        }

        /// <summary>
        /// Gets a hh:mm:ss value of a <see cref="TimeSpan"/> with rounded seconds
        /// </summary>
        /// <param name="val">The <see cref="TimeSpan"/></param>
        /// <returns>The hh:mm:ss value</returns>
        public static string GetHHMMSSFromTimeSpan(TimeSpan val)
        {
            var rounded = TimeSpan.FromSeconds(Math.Round(val.TotalSeconds, MidpointRounding.AwayFromZero));
            return $"{(int) Math.Floor(rounded.TotalHours):D2}:{rounded.Minutes:D2}:{rounded.Seconds:D2}";
        }

        public static string Generator =>
            $"{Assembly.GetExecutingAssembly().GetName().Name} v{Assembly.GetExecutingAssembly().GetName().Version}";

        private static readonly Processor Processor = new Processor();
        private static readonly XsltCompiler XsltCompiler = Processor.NewXsltCompiler();

        private static IDictionary<string, XsltExecutable> embeddedXsltExecutables;

        public static IDictionary<string, XsltExecutable> EmbeddedXsltExecutables
        {
            get
            {
                if (embeddedXsltExecutables == null)
                {
                    embeddedXsltExecutables = new Dictionary<string, XsltExecutable>();
                    //Write the embedded xslts to a temp dir
                    var temp = Path.GetTempFileName();
                    File.Delete(temp);
                    Directory.CreateDirectory(temp);
                    try
                    {
                        foreach (var xsltName in Assembly
                            .GetExecutingAssembly()
                            .GetManifestResourceNames()
                            .Where(n => n.StartsWith($"{typeof(Utils).Namespace}.Xslt.") && n.EndsWith(".xsl")))
                        {
                            using (var resStr = Assembly.GetExecutingAssembly().GetManifestResourceStream(xsltName))
                            {
                                using (var fs = new FileStream(
                                    Path.Combine(temp, xsltName.Substring((typeof(Utils).Namespace ?? "").Length + 6)),
                                    FileMode.CreateNew,
                                    FileAccess.Write))
                                {
                                    resStr?.CopyTo(fs);
                                }
                            }
                        }
                        foreach (var xsltFile in Directory.GetFiles(temp)
                            .Where(n => !Path.Combine(temp, "l10n.xsl").Equals(n)))
                        {
                            embeddedXsltExecutables.Add(
                                Path.GetFileName(xsltFile),
                                XsltCompiler.Compile(new Uri(xsltFile)));
                        }
                    }
                    finally
                    {
                        if (Directory.Exists(temp))
                        {
                            Directory.Delete(temp, true);
                        }
                    }

                }
                return new ReadOnlyDictionary<string, XsltExecutable>(embeddedXsltExecutables);
            }
        }

        private static IDictionary<string, XsltExecutable> dtbookToXhtmlTransformsByVersion;

        public static IDictionary<string, XsltExecutable> DtbookToXhtmlTransformsByVersion =>
            dtbookToXhtmlTransformsByVersion ?? (dtbookToXhtmlTransformsByVersion =
                new ReadOnlyDictionary<string, XsltExecutable>(
                    new Dictionary<string, XsltExecutable>()
                    {
                        {"1.1.0", EmbeddedXsltExecutables["dtbook110to2005-1.xsl"]},
                        {"2005-1", EmbeddedXsltExecutables["dtbook2005-1to2.xsl"]},
                        {"2005-2", EmbeddedXsltExecutables["dtbook2005-2to3.xsl"]},
                        {"2005-3", EmbeddedXsltExecutables["dtbook2xhtml.xsl"]}
                    }));

        public static XDocument TransformDtbookToXhtml(XDocument dtbook, bool deleteDualH1Title = true)
        {
            if (dtbook == null) throw new ArgumentNullException(nameof(dtbook));
            if (dtbook.Root?.Name.LocalName != "dtbook")
            {
                throw new InvalidOperationException("Input document is not a dtbook document");
            }
            var version = dtbook.Root.Attribute("version")?.Value ?? "";
            if (!DtbookToXhtmlTransformsByVersion.ContainsKey(version))
            {
                throw new InvalidOperationException($"dtbook version {version} not supported");
            }
            var input = Processor.NewDocumentBuilder().Build(dtbook.CreateReader());
            var strWr = new StringWriter();
            var serializer = Processor.NewSerializer(strWr);
            var trans = DtbookToXhtmlTransformsByVersion[version].Load();
            trans.InitialContextNode = input;
            trans.Run(serializer);
            var res = XDocument.Parse(strWr.ToString());
            if (res.Root?.Name.LocalName == "dtbook")
            {
                return TransformDtbookToXhtml(res, deleteDualH1Title);
            }
            if (deleteDualH1Title)
            {
                var firstH1 = res.Root?.Element(XhtmlNs + "body")?.Element(XhtmlNs + "h1");
                if (firstH1?.Attribute("class")?.Value.Split(' ').Contains("title") ?? false)
                {
                    var secondH1 = firstH1.ElementsAfterSelf().FirstOrDefault();
                    if (secondH1?.Name == XhtmlNs + "h1"
                        && (secondH1?.Attribute("class")?.Value.Split(' ').Contains("title") ?? false)
                        && firstH1.Value == secondH1?.Value)
                    {
                        secondH1.Remove();
                    }
                }

            }
            return res;
        }

        public static TimeSpan AdjustClipTime(TimeSpan clipTime, TimeSpan startOffset, double factor)
        {
            return startOffset + TimeSpan.FromSeconds((clipTime - startOffset).TotalSeconds * factor);
        }

        public static Dictionary<CultureInfo, string> PageNumberNamesByCulture =
            new Dictionary<CultureInfo, string>()
            {
                {new CultureInfo("en"), "Page"},
                {new CultureInfo("de"), "Seite"},
                {new CultureInfo("es"), "Página"},
                {new CultureInfo("fr"), "Page"},
                {new CultureInfo("da"), "Side"},
                {new CultureInfo("sv"), "Sida"},
                {new CultureInfo("no"), "Side"},
                {new CultureInfo("fi"), "Sivu"}
            };

        public static void AddPageName(XElement pageNumberElement)
        {
            if (pageNumberElement == null) return;
            var ci = SelectCulture(pageNumberElement);
            if (PageNumberNamesByCulture.TryGetValue(ci, out var pn))
            {
                if (!pageNumberElement.Value.StartsWith(pn, true, ci))
                {
                    pageNumberElement.Value = $"{pn} {pageNumberElement.Value}";
                }
            }
        }

        public static void RemovePageName(XElement pageNumberElement)
        {
            if (pageNumberElement == null) return;
            var ci = SelectCulture(pageNumberElement);
            if (PageNumberNamesByCulture.TryGetValue(ci, out var pn))
            {
                if (pageNumberElement.Value.StartsWith(pn, true, ci))
                {
                    pageNumberElement.Value = $"{pn} {pageNumberElement.Value}";
                }
            }
        }

        public static void TrimWhiteSpace(XElement element)
        {
            foreach (var text in element.DescendantNodes().OfType<XText>())
            {
                text.Value = Regex.Replace(text.Value, @"\s+", " ", RegexOptions.Singleline);
            }

            while (element.Nodes().FirstOrDefault() is XText t1 && String.IsNullOrWhiteSpace(t1.Value))
            {
                t1.Remove();
            }
            while (element.Nodes().LastOrDefault() is XText t2 && String.IsNullOrWhiteSpace(t2.Value))
            {
                t2.Remove();
            }
            if (element.Nodes().FirstOrDefault() is XText t3)
            {
                t3.Value = t3.Value.TrimStart();
            }
            if (element.Nodes().LastOrDefault() is XText t4)
            {
                t4.Value = t4.Value.TrimEnd();
            }

        }

        public static bool CopyXhtmlDocumentWithImages(
            XDocument xhtmlDoc,
            string outputDirectory,
            out string xhtmlPath,
            Func<int, string, bool> cancellableProgressDelegate = null)
        {
            if (xhtmlDoc == null) throw new ArgumentNullException(nameof(xhtmlDoc));
            if (outputDirectory == null) throw new ArgumentNullException(nameof(outputDirectory));
            if (cancellableProgressDelegate == null) cancellableProgressDelegate = (i, s) => false;
            xhtmlPath = null;
            if (outputDirectory == null)
            {
                throw new ApplicationException("No output directory was given");
            }
            if (!Directory.Exists(outputDirectory))
            {
                throw new ApplicationException($"Output directory {outputDirectory} does not exist");
            }

            var entries = new DirectoryInfo(outputDirectory).GetFileSystemInfos();
            for (int i = 0; i < entries.Length; i++)
            {
                if (cancellableProgressDelegate(100 * i / entries.Length,
                    $"Emptying output directory {outputDirectory} (entry {i}/{entries.Length})"))
                {
                    return false;
                }
                if (entries[i] is DirectoryInfo di)
                {
                    di.Delete(true);
                }
                if (entries[i] is FileInfo fi)
                {
                    fi.Delete();
                }
            }
            xhtmlPath = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(new Uri(xhtmlDoc.BaseUri).LocalPath) + ".html");
            var xhtmlUri = new Uri(xhtmlPath);
            var sourceUri = new Uri(xhtmlDoc.BaseUri);
            var imageSrcs = xhtmlDoc
                .Descendants(Utils.XhtmlNs + "img")
                .Select(img => img.Attribute("src")?.Value)
                .Where(src => !String.IsNullOrWhiteSpace(src))
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .Where(src => Uri.IsWellFormedUriString(src, UriKind.Relative))
                .ToArray();
            for (int i = 0; i < imageSrcs.Length; i++)
            {
                if (cancellableProgressDelegate(100 * i / imageSrcs.Length,
                    $"Copying image {imageSrcs[i]} (entry {i}/{imageSrcs.Length})"))
                {
                    return false;
                }
                var source = new Uri(sourceUri, imageSrcs[i]).LocalPath;
                var dest = new Uri(xhtmlUri, imageSrcs[i]).LocalPath;
                var destDir = Path.GetDirectoryName(dest);
                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                File.Copy(source, dest);
            }
            xhtmlDoc.Save(xhtmlPath);
            return true;
        }

        public static string GetFileName(XObject node)
        {
            if (String.IsNullOrEmpty(node.BaseUri))
            {
                return "";
            }
            return Path.GetFileName(new Uri(node.BaseUri).LocalPath);
        }

        public static string GetFirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(v => !String.IsNullOrEmpty(v)) ?? "";
        }

        /// <summary>
        /// Gets the <see cref="Uri"/> represented by a <see cref="XAttribute"/>
        /// </summary>
        /// <param name="uriAttr">The <see cref="XAttribute"/></param>
        /// <returns>
        /// The <see cref="Uri"/> represented by <paramref name="uriAttr"/>.T
        /// The <see cref="XObject.BaseUri"/> of is used as base of the returned <see cref="Uri"/>, if present.
        /// If <paramref name="uriAttr"/> is <c>null</c>, <c>null</c> is returned
        /// </returns>
        public static Uri GetUri(XAttribute uriAttr)
        {
            if (uriAttr == null)
            {
                return null;
            }

            if (String.IsNullOrEmpty(uriAttr.BaseUri))
            {
                return new Uri(uriAttr.Value);
            }
            return new Uri(new Uri(uriAttr.BaseUri, UriKind.Absolute), uriAttr.Value);
        }


        /// <summary>
        /// Parses a SMIL 3.0 file clip attribute value (only "n.nnns" form is supported)
        /// </summary>
        /// <param name="val">The value</param>
        /// <returns>The <see cref="TimeSpan"/> equivalent of the value</returns>
        public static TimeSpan ParseSmilClip(string val)
        {
            if (val == null) throw new ArgumentNullException(nameof(val));
            if (String.IsNullOrWhiteSpace(val))
            {
                throw new ArgumentException($"Value is empty", nameof(val));
            }
            val = val.Trim();
            if (val.EndsWith("s"))
            {
                var secs = Double.Parse(
                    val.Substring(0, val.Length - 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture);
                return TimeSpan.FromSeconds(secs);
            }
            else
            {
                throw new ArgumentException($"Value {val} is not a valid Daisy 2.02 smil clip value", nameof(val));
            }
        }

        public static TimeSpan GetAudioDuration(XElement audio)
        {
            var clipBegin = ParseSmilClip(audio.Attribute("clipBegin")?.Value ?? "0s");
            var clipEnd = ParseSmilClip(audio.Attribute("clipEnd")?.Value ?? "0s");
            return clipEnd.Subtract(clipBegin);
        }

        public static IEnumerable<CultureInfo> GetCultures(XDocument doc)
        {
            return doc
                .Descendants()
                .Select(Utils.GetLanguage)
                .Where(v => !String.IsNullOrWhiteSpace(v))
                .Select(lang => new CultureInfo(lang))
                .Distinct();
        }

        public static XElement GetEpubUniqueIdentifierElement(XDocument packageFile, bool create = false)
        {
            var result = packageFile
                .Descendants(DcNs + "identifier")
                .FirstOrDefault(dcId =>
                    (dcId.Attribute("id")?.Value ?? "") ==
                    (packageFile.Root?.Attribute("unique-identifier")?.Value ?? ""));
            if (result == null && create)
            {
                var metadata = packageFile.Descendants(OpfNs + "metadata").FirstOrDefault();
                if (metadata != null)
                {
                    var id = Utils.GenerateNewId(packageFile);
                    result = new XElement(
                        DcNs + "identifier",
                        new XAttribute("id", id));
                    metadata.AddFirst(result);
                    packageFile.Root?.SetAttributeValue("unique-identifier", id);

                }
            }
            return result;
        }

        public static XElement GetEpubMetadataElement(XDocument packageFile, string name, bool create = false)
        {
            if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));
            if (name == null) throw new ArgumentNullException(nameof(name));
            XElement result = null;
            var metadata = packageFile.Descendants(OpfNs + "metadata").FirstOrDefault();
            if (name.StartsWith("dc:"))
            {
                result = packageFile
                    .Descendants(DcNs + name.Substring(3))
                    .FirstOrDefault(dcId =>
                        (dcId.Attribute("id")?.Value ?? "") ==
                        (packageFile.Root?.Attribute("unique-identifier")?.Value ?? ""));
                if (result == null && create && metadata != null)
                {
                    result = new XElement(DcNs + name.Substring(3));
                    metadata.Add(result);
                }
            }
            else
            {
                result = packageFile
                    .Descendants(OpfNs + "meta")
                    .FirstOrDefault(meta => (meta.Attribute("name")?.Value ?? "") == name);
                if (result == null && create && metadata != null)
                {
                    result = new XElement(OpfNs + "meta");
                    metadata.Add(result);
                }
            }
            return result;
        }
    }
}
