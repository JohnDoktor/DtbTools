﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DtbMerger2Library.Daisy202
{
    public class DtbBuilder
    {
        public TimeSpan AllowedFileEndAudio { get; set; } = TimeSpan.FromSeconds(1.5);

        public List<MergeEntry> MergeEntries { get; private set; }

        public DtbBuilder() : this(new MergeEntry[0])
        {
            MergeEntries = new List<MergeEntry>();
        }

        public DtbBuilder(IEnumerable<MergeEntry> entries)
        {
            MergeEntries = new List<MergeEntry>(entries);
            ContentDocumentName = "text.html";
            NccDocumentName = "ncc.html";
        }

        private string GetSmilFileName(int index)
        {
            return $"SM{index:D5}.smil";
        }

        public string AudioFileExtension =>
            Path.GetExtension(audioFileSegments.FirstOrDefault()?.FirstOrDefault()?.AudioFile.AbsolutePath.ToLowerInvariant());

        private string GetAudioFileName(int index)
        {
            return $"AUD{index:D5}{AudioFileExtension}";
        }

        private List<XDocument> smilFiles = new List<XDocument>();

        private List<List<AudioSegment>> audioFileSegments = new List<List<AudioSegment>>();

        public IDictionary<string, List<AudioSegment>> AudioFileSegments => Enumerable.Range(0, audioFileSegments.Count)
            .ToDictionary(GetAudioFileName, i => audioFileSegments[i]);

        public IDictionary<string, XDocument> SmilFiles => Enumerable.Range(0, smilFiles.Count)
            .ToDictionary(GetSmilFileName, i => smilFiles[i]);

        public string ContentDocumentName { get; private set; }

        public XDocument ContentDocument { get; private set; }

        public string NccDocumentName { get; private set; }

        public XDocument NccDocument { get; private set; }

        public IDictionary<string, XDocument> XmlDocuments => SmilFiles
            .Union(new[] {
                new KeyValuePair<String, XDocument>(NccDocumentName, NccDocument),
                new KeyValuePair<String, XDocument>(ContentDocumentName, ContentDocument)})
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        public void ResetBuilder()
        {
            smilFiles.Clear();
            audioFileSegments.Clear();
            ContentDocument = null;
            NccDocument = Utils.GenerateSkeletonXhtmlDocument();
            index = 0;
            nccIdIndex = 0;
            contentIdIndex = 0;
            totalElapsedTime = TimeSpan.Zero;
        }

        private int index = 0;
        private int nccIdIndex = 0;
        private int contentIdIndex = 0;
        private TimeSpan totalElapsedTime = TimeSpan.Zero;

        private List<XElement> GetNccElements(MergeEntry me, List<XElement> smilElements)
        {
            var nccElements = me.GetNccElements().Select(Utils.CloneWithBaseUri).ToList();
            //Set new id attributes on ncc elements, fixing references from smil <text> elements
            foreach (var idAttr in nccElements
                .SelectMany(e => e.DescendantsAndSelf())
                .Select(e => e.Attribute("id"))
                .Where(attr => attr != null))
            {
                var newId = $"NCCID{nccIdIndex:D5}";
                nccIdIndex++;
                //Find and fix smil <text> elements src attributes, that link to ncc elements
                foreach (var textSrcAttr in smilElements
                    .Select(e => e.Element("text")?.Attribute("src"))
                    .Where(attr => Utils.IsReferenceTo(Utils.GetUri(attr), new Uri(me.Ncc.BaseUri))))
                {
                    var textSrcUri = Utils.GetUri(textSrcAttr);
                    if (textSrcUri.Fragment == $"#{idAttr.Value}")
                    {
                        textSrcAttr.Value = $"{NccDocumentName}#{newId}";
                    }
                }
                idAttr.Value = newId;
            }
            //Fix <a> element descendants of ncc elements, that point to the smil file
            foreach (var nccAHrefAttr in nccElements
                .SelectMany(e => e.DescendantsAndSelf(e.Name.Namespace + "a"))
                .Select(a => a.Attribute("href"))
                .Where(href => href != null))
            {
                var uri = Utils.GetUri(nccAHrefAttr);
                if (Utils.IsReferenceTo(uri, new Uri(me.Smil.BaseUri)))
                {
                    nccAHrefAttr.Value = $"{GetSmilFileName(index)}{uri.Fragment}";
                }
            }

            return nccElements;
        }

        public List<XElement> GetContentElements(MergeEntry me, List<XElement> smilElements)
        {
            var contentElements = me.GetTextElements().Select(Utils.CloneWithBaseUri).ToList();
            if (contentElements.Any())
            {
                //Set new id attributes on content elements, fixing references from smil <text> elements
                foreach (var idAttr in contentElements
                    .SelectMany(e => e.DescendantsAndSelf())
                    .Select(e => e.Attribute("id"))
                    .Where(attr => attr != null))
                {
                    var newId = $"TEXTID{contentIdIndex:D5}";
                    contentIdIndex++;
                    //Find and fix smil <text> elements src attributes, that link to content elements
                    foreach (var textSrcAttr in smilElements
                        .Select(e => e.Element("text")?.Attribute("src"))
                        .Where(attr => me.ContentDocuments.Values.Any(cd =>
                            Utils.IsReferenceTo(Utils.GetUri(attr), new Uri(cd.BaseUri)))))
                    {
                        var textSrcUri = Utils.GetUri(textSrcAttr);
                        if (textSrcUri.Fragment == $"#{idAttr.Value}")
                        {
                            textSrcAttr.Value = $"{ContentDocumentName}#{newId}";
                        }
                    }
                    idAttr.Value = newId;
                }
                //Fix <a> element descendants of ncc elements, that point to the smil file
                foreach (var contentAHrefAttr in contentElements
                    .SelectMany(e => e.DescendantsAndSelf(e.Name.Namespace + "a"))
                    .Select(a => a.Attribute("href"))
                    .Where(href => href != null))
                {
                    var uri = Utils.GetUri(contentAHrefAttr);
                    if (Utils.IsReferenceTo(uri, new Uri(me.Smil.BaseUri)))
                    {
                        contentAHrefAttr.Value = $"{GetSmilFileName(index)}{uri.Fragment}";
                    }
                }
            }

            return contentElements;
        }

        public void BuildDtb()
        {
            if (!MergeEntries.Any())
            {
                throw new InvalidOperationException("No merge entries added to builder");
            }
            ResetBuilder();
            var generator =
                $"{Assembly.GetExecutingAssembly().GetName().Name} v{Assembly.GetExecutingAssembly().GetName().Version}";

            var entries = MergeEntries.SelectMany(entry => entry.DescententsAndSelf).ToList();
            if (entries.Any(me => me.GetTextElements().Any()))
            {
                ContentDocument = Utils.GenerateSkeletonXhtmlDocument();
            }

            var identifier = Utils.CreateOrGetMeta(entries.First().Ncc, "dc:identifier")?.Attribute("content")?.Value ??
                             Guid.NewGuid().ToString();
            foreach (var me in entries)
            {
                var smilFile = Utils.GenerateSkeletonSmilDocument();
                var smilElements = me.GetSmilElements().Select(Utils.CloneWithBaseUri).ToList();
                NccDocument.Root?.Element(NccDocument?.Root.Name.Namespace + "body")?.Add(GetNccElements(me, smilElements));

                var contentElements = GetContentElements(me, smilElements);
                if (contentElements.Any())
                {
                    ContentDocument.Root?.Element(ContentDocument?.Root.Name.Namespace + "body")?.Add(contentElements);
                }

                audioFileSegments.Add(me.GetAudioSegments().ToList());

                var timeInThisSmil = TimeSpan.FromSeconds(smilElements
                    .SelectMany(e => e.Descendants("audio"))
                    .Select(audio => Utils
                        .ParseSmilClip(audio.Attribute("clip-end")?.Value)
                        .Subtract(Utils.ParseSmilClip(audio.Attribute("clip-begin")?.Value)).TotalSeconds)
                    .Sum());
                Utils.CreateOrGetMeta(smilFile, "ncc:totalElapsedTime")?.SetAttributeValue(
                    "content", Utils.GetHHMMSSFromTimeSpan(totalElapsedTime));
                Utils.CreateOrGetMeta(smilFile, "ncc:timeInThisSmil")?.SetAttributeValue(
                    "content", Utils.GetHHMMSSFromTimeSpan(timeInThisSmil));
                var seq = smilFile.Root?.Element("body")?.Element("seq");
                if (seq == null)
                {
                    throw new ApplicationException("Generated smil document contains no main seq");
                }
                seq.SetAttributeValue(
                    "dur", 
                    $"{timeInThisSmil.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)}s");
                Utils.CreateOrGetMeta(smilFile, "ncc:generator")?.SetAttributeValue("content", generator);
                Utils.CreateOrGetMeta(smilFile, "dc:identifier")?.SetAttributeValue("content", identifier);
                seq.Add(smilElements);
                smilFiles.Add(smilFile);
                totalElapsedTime += TimeSpan.FromSeconds(Math.Ceiling(timeInThisSmil.TotalSeconds));
                index++;
            }

            NccDocument.Root?.Element(Utils.XhtmlNs+"head")?.Add(
                entries.First().Ncc.Root?.Element(Utils.XhtmlNs + "head")?.Elements(Utils.XhtmlNs + "meta"));

            Utils.CreateOrGetMeta(NccDocument, "ncc:totalTime")?.SetAttributeValue("content", Utils.GetHHMMSSFromTimeSpan(totalElapsedTime));
            var fileCount =
                1
                + 2 * smilFiles.Count
                + (ContentDocument == null ? 0 : 1)
                + entries.Select(me => me.GetMediaEntries().Count()).Sum();
            Utils.CreateOrGetMeta(NccDocument, "ncc:files")?.SetAttributeValue("content", fileCount);
            Utils.CreateOrGetMeta(NccDocument, "ncc:depth")?.SetAttributeValue(
                "content",
                entries.Select(me => me.Depth).Max());
            Utils.CreateOrGetMeta(NccDocument, "ncc:tocItems")?.SetAttributeValue(
                "content",
                entries.Count);
            foreach (var pt in new[] {"Front", "Normal", "Special"})
            {
                Utils.CreateOrGetMeta(NccDocument, $"ncc:page{pt}")?.SetAttributeValue(
                    "content",
                    entries.SelectMany(me => 
                        me.GetNccElements()
                            .Where(e => 
                                e.Name == Utils.XhtmlNs + "span" 
                                && e.Attribute("class")?.Value == $"page-{pt.ToLowerInvariant()}"))
                    .Count());
            }
            Utils.CreateOrGetMeta(NccDocument, "ncc:multimediaType")?.SetAttributeValue(
                "content",
                ContentDocument==null? "audioNCC" : "audioFullText");
            Utils.CreateOrGetMeta(NccDocument, "ncc:generator")?.SetAttributeValue("content", generator);
        }

        public void SaveDtb(string baseDir)
        {
            if (Directory.Exists(baseDir))
            {
                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    Directory.Delete(dir, true);
                }

                foreach (var file in Directory.GetFiles(baseDir))
                {
                    File.Delete(file);
                }
            }
            Directory.CreateDirectory(baseDir);
            var xmlDocs = XmlDocuments;
            foreach (var xmlFileName in xmlDocs.Keys)
            {
                xmlDocs[xmlFileName].Save(Path.Combine(baseDir, xmlFileName), SaveOptions.OmitDuplicateNamespaces);
            }

            foreach (var audioFileName in AudioFileSegments.Keys)
            {
                if (AudioFileSegments[audioFileName].Count == 1)
                    //&& AudioFileSegments[audioFileName][0].ClipBegin == TimeSpan.Zero
                    //&& Path.GetExtension(AudioFileSegments[audioFileName][0].AudioFile.AbsolutePath).ToLowerInvariant() == AudioFileExtension)
                {
                    var audSeg = AudioFileSegments[audioFileName].First();
                    if (audSeg.AudioFileDuration < audSeg.ClipEnd)
                    {
                        throw new InvalidOperationException(
                            $"Audio segment clip-end {audSeg.ClipEnd} is beyond the end of audio file {audSeg.AudioFile}");
                    }
                    if (audSeg.AudioFileDuration < audSeg.ClipEnd.Add(AllowedFileEndAudio))
                    {
                        File.Copy(Uri.UnescapeDataString(AudioFileSegments[audioFileName][0].AudioFile.LocalPath), Path.Combine(baseDir, audioFileName));
                        continue;
                    }
                }
                //Now we need to edit audio files - not yet supported
                throw new NotSupportedException("Only DTBs with one mp3/wav file per heading of default type is currently supported");
            }
        }
    }
}
