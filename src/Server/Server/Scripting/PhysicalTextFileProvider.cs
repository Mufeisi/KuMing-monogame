using System.Text;
using Server.MirEnvir;

namespace Server.Scripting
{
    public enum TxtScriptLayout
    {
        LyoCrystal,
        LingFeng
    }

    public sealed class PhysicalTextFileProviderOptions
    {
        public PhysicalTextFileProviderOptions(string rootPath, TxtScriptLayout layout)
        {
            RootPath = rootPath;
            Layout = layout;
        }

        public string RootPath { get; }

        public TxtScriptLayout Layout { get; }

        public long MaxFileBytes { get; init; } = 1024 * 1024;
    }

    public sealed record LingFengEnvirPreflightSummary(
        int Accepted,
        int RuntimeData,
        int ExternalDependency,
        int Rejected);

    public sealed class PhysicalTextFileProvider : ITextFileProvider
    {
        private static readonly HashSet<string> LyoCrystalAllowedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "NPCs",
            "QuestDiary",
            "SystemScripts",
            "Defines",
            "Variables"
        };

        private readonly IReadOnlyDictionary<string, TextFileDefinition> _definitions;
        private readonly TextFileDefinition[] _all;

        internal IDropTableProvider MonsterDropProvider { get; }
        internal LingFengMonsterContentProvider MonsterContentProvider { get; }
        internal LingFengWorldContentProvider WorldContentProvider { get; }
        internal LingFengCommerceContentProvider CommerceContentProvider { get; }
        internal LingFengRuleListContentProvider RuleListContentProvider { get; }
        internal IReadOnlyList<TextFileDefinition> CommerceSourceDefinitions { get; }
        internal IReadOnlyList<string> DomainDiagnostics { get; }
        public LingFengExternalDependencyManifest ExternalDependencyManifest { get; }
        public LingFengEnvirPreflightSummary PreflightSummary { get; }

        public PhysicalTextFileProvider(PhysicalTextFileProviderOptions options)
            : this(options, null)
        {
        }

        internal PhysicalTextFileProvider(
            PhysicalTextFileProviderOptions options,
            IEnumerable<string> candidateFiles)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.MaxFileBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxFileBytes), "TXT 单文件大小上限必须大于 0 字节。");
            if (!Enum.IsDefined(options.Layout))
                throw new ArgumentOutOfRangeException(nameof(options.Layout), $"未知的 TXT 布局：{options.Layout}");

            string root = Path.GetFullPath(options.RootPath ?? string.Empty);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"TXT 脚本根目录不存在：{root}");

            IEnumerable<string> files = candidateFiles ?? Directory.EnumerateFiles(
                root,
                options.Layout == TxtScriptLayout.LingFeng ? "*" : "*.txt",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = false,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MatchCasing = MatchCasing.CaseInsensitive
                });

            var definitions = new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);
            var monsterDrops = new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);
            var monsterUseItems = new List<TextFileDefinition>();
            var smartMonsters = new List<TextFileDefinition>();
            TextFileDefinition mapInfo = null;
            TextFileDefinition mongen = null;
            TextFileDefinition mapQuest = null;
            var mapQuestScripts = new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);
            TextFileDefinition shopItems = null;
            TextFileDefinition makeItems = null;
            var ruleLists = new Dictionary<string, TextFileDefinition>(StringComparer.Ordinal);
            var domainDiagnostics = new List<string>();
            var externalDependencies = new List<LingFengDependencyRequirement>();
            int acceptedFiles = 0;
            int runtimeDataFiles = 0;
            int externalDependencyFiles = 0;
            foreach (string file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string fullFile = Path.GetFullPath(file);
                if (!IsWithinRoot(root, fullFile))
                    throw new InvalidDataException($"TXT 文件超出配置根目录：{fullFile}（root={root}）");
                if ((File.GetAttributes(fullFile) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"TXT 文件不允许使用符号链接或重解析点：{fullFile}");

                string relative = Path.GetRelativePath(root, fullFile);
                if (options.Layout == TxtScriptLayout.LingFeng)
                {
                    if (TryMapCommerceSourceKey(relative, out string commerceKey,
                            out LingFengCommerceSourceKind commerceKind))
                    {
                        acceptedFiles++;
                        TextFileDefinition commerceDefinition = ReadDefinition(
                            commerceKey, fullFile, options.MaxFileBytes);
                        if (commerceKind == LingFengCommerceSourceKind.ShopItems)
                        {
                            if (shopItems != null)
                                throw new InvalidDataException($"重复的翎风商城配置：{fullFile}");
                            shopItems = commerceDefinition;
                        }
                        else if (commerceKind == LingFengCommerceSourceKind.MakeItems)
                        {
                            if (makeItems != null)
                                throw new InvalidDataException($"重复的翎风配方配置：{fullFile}");
                            makeItems = commerceDefinition;
                        }
                        else if (!ruleLists.TryAdd(commerceKey, commerceDefinition))
                            throw new InvalidDataException($"重复的翎风规则名单：{commerceKey}；来源：{fullFile}");
                        continue;
                    }
                    if (TryMapWorldSourceKey(relative, out string worldKey))
                    {
                        acceptedFiles++;
                        TextFileDefinition worldDefinition = ReadDefinition(worldKey, fullFile, options.MaxFileBytes);
                        switch (worldKey)
                        {
                            case "world/mapinfo" when mapInfo == null: mapInfo = worldDefinition; break;
                            case "world/mongen" when mongen == null: mongen = worldDefinition; break;
                            case "world/mapquest" when mapQuest == null: mapQuest = worldDefinition; break;
                            default:
                                throw new InvalidDataException($"重复的翎风世界配置：{worldKey}；来源：{fullFile}");
                        }
                        continue;
                    }
                    if (TryMapMapQuestScriptKey(relative, out string mapQuestKey))
                    {
                        acceptedFiles++;
                        TextFileDefinition page = ReadDefinition(mapQuestKey, fullFile, options.MaxFileBytes);
                        if (!mapQuestScripts.TryAdd(mapQuestKey, page))
                            throw new InvalidDataException($"重复的地图任务脚本逻辑 Key：{mapQuestKey}；来源：{fullFile}");
                        continue;
                    }
                    LingFengEnvirFileClassification classification =
                        LingFengEnvirFileClassifier.Classify(relative);
                    if (classification.Owner == LingFengEnvirFileOwner.Unassigned)
                        throw new InvalidDataException(
                            $"翎风 Envir 文件未归属：{relative}（规则：{classification.RuleId}）");
                    if (!classification.MayPublishAsScript)
                    {
                        bool mappedDomainConfiguration = false;
                        if (classification.Owner == LingFengEnvirFileOwner.DomainConfiguration &&
                            TryMapMonsterDropKey(relative, out string dropKey))
                        {
                            mappedDomainConfiguration = true;
                            TextFileDefinition dropDefinition = ReadDefinition(
                                dropKey, fullFile, options.MaxFileBytes);
                            if (!monsterDrops.TryAdd(dropKey, dropDefinition))
                            {
                                TextFileDefinition existing = monsterDrops[dropKey];
                                if (TryResolveWhitespaceAlias(monsterDrops, dropKey, existing, dropDefinition,
                                        out string diagnostic))
                                {
                                    domainDiagnostics.Add(diagnostic);
                                    continue;
                                }
                                throw new InvalidDataException(
                                    $"重复的怪物掉落逻辑 Key：{dropKey}；来源：{existing.SourcePath}；{dropDefinition.SourcePath}");
                            }
                        }
                        else if (classification.Owner == LingFengEnvirFileOwner.DomainConfiguration &&
                                 TryMapMonsterContentKey(relative, "MonUseItems", out string useItemKey))
                        {
                            mappedDomainConfiguration = true;
                            monsterUseItems.Add(ReadDefinition(useItemKey, fullFile, options.MaxFileBytes));
                        }
                        else if (classification.Owner == LingFengEnvirFileOwner.DomainConfiguration &&
                                 TryMapMonsterContentKey(relative, "SmartMonster", out string smartKey))
                        {
                            mappedDomainConfiguration = true;
                            smartMonsters.Add(ReadDefinition(smartKey, fullFile, options.MaxFileBytes));
                        }
                        string normalizedRelative = relative.Replace('\\', '/');
                        if (classification.Owner == LingFengEnvirFileOwner.ClientContract)
                        {
                            externalDependencyFiles++;
                            externalDependencies.Add(new LingFengDependencyRequirement(
                                LingFengDependencyKind.ClientContract, normalizedRelative,
                                LingFengDependencyLevel.E2, normalizedRelative));
                        }
                        else if ((classification.Owner == LingFengEnvirFileOwner.DomainConfiguration &&
                                  !mappedDomainConfiguration) || IsExternalDomainDependency(normalizedRelative))
                        {
                            externalDependencyFiles++;
                            externalDependencies.Add(new LingFengDependencyRequirement(
                                LingFengDependencyKind.DomainAdapter, normalizedRelative,
                                LingFengDependencyLevel.E2, normalizedRelative));
                        }
                        else if (mappedDomainConfiguration)
                            acceptedFiles++;
                        else
                            runtimeDataFiles++;
                        continue;
                    }
                    acceptedFiles++;
                    TextFileDefinition classifiedDefinition = ReadDefinition(
                        classification.LogicKey, fullFile, options.MaxFileBytes);
                    if (!definitions.TryAdd(classification.LogicKey, classifiedDefinition))
                    {
                        TextFileDefinition existing = definitions[classification.LogicKey];
                        if (TryResolveQFunctionAlias(
                                root, definitions, classification.LogicKey, existing, classifiedDefinition))
                            continue;
                        throw new InvalidDataException(
                            $"重复的物理 TXT 逻辑 Key：{classification.LogicKey}；来源：{existing.SourcePath}；{classifiedDefinition.SourcePath}");
                    }
                    continue;
                }
                if (!TryMapLogicKey(options.Layout, relative, out string key)) continue;
                TextFileDefinition definition = ReadDefinition(key, fullFile, options.MaxFileBytes);
                if (!definitions.TryAdd(key, definition))
                {
                    TextFileDefinition existing = definitions[key];
                    throw new InvalidDataException(
                        $"重复的物理 TXT 逻辑 Key：{key}；来源：{existing.SourcePath}；{definition.SourcePath}");
                }
            }

            DomainDiagnostics = domainDiagnostics.AsReadOnly();
            if (options.Layout == TxtScriptLayout.LingFeng && monsterDrops.Count > 0)
            {
                if (!LingFengMonsterDropProvider.TryCreate(
                        monsterDrops.Values,
                        definitions,
                        Envir.Main.GetItemInfo,
                        out LingFengMonsterDropProvider dropProvider,
                        out IReadOnlyList<string> errors))
                    throw new InvalidDataException(string.Join(Environment.NewLine, errors));
                MonsterDropProvider = dropProvider;
            }
            if (options.Layout == TxtScriptLayout.LingFeng && (monsterUseItems.Count > 0 || smartMonsters.Count > 0))
            {
                if (!LingFengMonsterContentProvider.TryCreate(monsterUseItems, smartMonsters,
                        out LingFengMonsterContentProvider contentProvider, out IReadOnlyList<string> errors))
                    throw new InvalidDataException(string.Join(Environment.NewLine, errors));
                MonsterContentProvider = contentProvider;
            }
            if (options.Layout == TxtScriptLayout.LingFeng &&
                (mapInfo != null || mongen != null || mapQuest != null))
            {
                if (mapInfo == null || mongen == null)
                    throw new InvalidDataException("LFENV12-WORLD-001：世界候选必须同时包含根目录 MapInfo.txt 与 Mongen.txt。");
                if (!LingFengWorldContentProvider.TryCreate(
                        mapInfo, mongen, mapQuest, mapQuestScripts,
                        out LingFengWorldContentProvider worldProvider,
                        out IReadOnlyList<string> worldErrors))
                    throw new InvalidDataException(string.Join(Environment.NewLine, worldErrors));
                WorldContentProvider = worldProvider;
                foreach (string scriptKey in worldProvider.MapQuests.Select(rule => rule.ScriptKey).Distinct(StringComparer.Ordinal))
                {
                    TextFileDefinition page = mapQuestScripts[scriptKey];
                    if (!definitions.TryAdd(scriptKey, page))
                        throw new InvalidDataException($"地图任务脚本与已有脚本逻辑 Key 冲突：{scriptKey}；来源：{page.SourcePath}");
                }
            }
            if (options.Layout == TxtScriptLayout.LingFeng &&
                (shopItems != null || makeItems != null))
            {
                if (!LingFengCommerceContentProvider.TryCreate(
                        shopItems, makeItems,
                        out LingFengCommerceContentProvider commerceProvider,
                        out IReadOnlyList<string> commerceErrors))
                    throw new InvalidDataException(string.Join(Environment.NewLine, commerceErrors));
                CommerceContentProvider = commerceProvider;
                domainDiagnostics.AddRange(commerceProvider.CompatibilityDiagnostics);
            }
            if (options.Layout == TxtScriptLayout.LingFeng && ruleLists.Count > 0)
            {
                if (!LingFengRuleListContentProvider.TryCreate(
                        ruleLists.Values, out LingFengRuleListContentProvider ruleProvider,
                        out IReadOnlyList<string> ruleErrors))
                    throw new InvalidDataException(string.Join(Environment.NewLine, ruleErrors));
                RuleListContentProvider = ruleProvider;
            }
            CommerceSourceDefinitions = Array.AsReadOnly(
                new[] { shopItems, makeItems }
                    .Where(value => value != null)
                    .Concat(ruleLists.Values)
                    .OrderBy(value => value.Key, StringComparer.Ordinal)
                    .ToArray());
            if (MonsterDropProvider is LingFengMonsterDropProvider lingFengDrops)
                externalDependencies.AddRange(lingFengDrops.GetDependencyRequirements());
            if (MonsterContentProvider != null)
                externalDependencies.AddRange(MonsterContentProvider.GetDependencyRequirements());
            if (WorldContentProvider != null)
                externalDependencies.AddRange(WorldContentProvider.GetDependencyRequirements());
            if (CommerceContentProvider != null)
                externalDependencies.AddRange(CommerceContentProvider.GetDependencyRequirements());
            if (options.Layout == TxtScriptLayout.LingFeng)
                externalDependencies.AddRange(LingFengScriptDependencyExtractor.Extract(definitions.Values));
            ExternalDependencyManifest = new LingFengExternalDependencyManifest(externalDependencies);
            PreflightSummary = new LingFengEnvirPreflightSummary(
                acceptedFiles, runtimeDataFiles, externalDependencyFiles, 0);
            _definitions = definitions;
            _all = definitions.Values.ToArray();
        }

        private enum LingFengCommerceSourceKind
        {
            ShopItems,
            MakeItems,
            RuleList
        }

        private static bool IsExternalDomainDependency(string relativePath) =>
            relativePath.StartsWith("Market_Prices/", StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith("Market_Upg/", StringComparison.OrdinalIgnoreCase);

        private static bool TryMapCommerceSourceKey(
            string relativePath,
            out string key,
            out LingFengCommerceSourceKind kind)
        {
            key = null;
            kind = default;
            string normalized = (relativePath ?? string.Empty).Replace('\\', '/');
            if (normalized.IndexOf('/') >= 0 ||
                !normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return false;
            string stem = Path.GetFileNameWithoutExtension(normalized);
            if (stem.Equals("Shopitemlist", StringComparison.OrdinalIgnoreCase))
            {
                key = "commerce/shopitems";
                kind = LingFengCommerceSourceKind.ShopItems;
                return true;
            }
            if (stem.Equals("Makeitem", StringComparison.OrdinalIgnoreCase))
            {
                key = "commerce/makeitem";
                kind = LingFengCommerceSourceKind.MakeItems;
                return true;
            }
            if (stem.Equals("Myshopitems", StringComparison.OrdinalIgnoreCase))
            {
                key = "rulelists/myshopitems";
                kind = LingFengCommerceSourceKind.RuleList;
                return true;
            }
            if (!stem.EndsWith("list", StringComparison.OrdinalIgnoreCase) &&
                !stem.StartsWith("allow", StringComparison.OrdinalIgnoreCase) &&
                !stem.StartsWith("deny", StringComparison.OrdinalIgnoreCase) &&
                !stem.StartsWith("disable", StringComparison.OrdinalIgnoreCase) &&
                !stem.StartsWith("enable", StringComparison.OrdinalIgnoreCase) &&
                !stem.StartsWith("filter", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!LogicKey.TryNormalize("RuleLists/" + stem, out key)) return false;
            kind = LingFengCommerceSourceKind.RuleList;
            return true;
        }

        private static bool TryMapWorldSourceKey(string relativePath, out string key)
        {
            string normalized = (relativePath ?? string.Empty).Replace('\\', '/');
            if (normalized.Equals("MapInfo.txt", StringComparison.OrdinalIgnoreCase))
                key = "world/mapinfo";
            else if (normalized.Equals("Mongen.txt", StringComparison.OrdinalIgnoreCase))
                key = "world/mongen";
            else if (normalized.Equals("MapQuest.txt", StringComparison.OrdinalIgnoreCase))
                key = "world/mapquest";
            else
            {
                key = null;
                return false;
            }
            return true;
        }

        private static bool TryMapMapQuestScriptKey(string relativePath, out string key)
        {
            key = null;
            string normalized = (relativePath ?? string.Empty).Replace('\\', '/');
            const string prefix = "MapQuest_def/";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return false;
            string nested = normalized[prefix.Length..^4];
            if (nested.Equals("QManage", StringComparison.OrdinalIgnoreCase)) return false;
            return LogicKey.TryNormalize("MapQuests/" + nested, out key);
        }

        private static bool TryMapMonsterDropKey(string relativePath, out string key)
        {
            key = null;
            string normalized = (relativePath ?? string.Empty).Replace('\\', '/');
            const string prefix = "MonItems/";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return false;
            string nested = normalized[prefix.Length..^4];
            return LogicKey.TryNormalize("Drops/" + nested, out key);
        }

        private static bool TryResolveWhitespaceAlias(
            IDictionary<string, TextFileDefinition> definitions,
            string key,
            TextFileDefinition existing,
            TextFileDefinition candidate,
            out string diagnostic)
        {
            diagnostic = null;
            string existingStem = Path.GetFileNameWithoutExtension(existing.SourcePath);
            string candidateStem = Path.GetFileNameWithoutExtension(candidate.SourcePath);
            bool existingCanonical = existingStem.Equals(existingStem.Trim(), StringComparison.Ordinal);
            bool candidateCanonical = candidateStem.Equals(candidateStem.Trim(), StringComparison.Ordinal);
            if (existingCanonical == candidateCanonical ||
                !existingStem.Trim().Equals(candidateStem.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            TextFileDefinition selected = candidateCanonical ? candidate : existing;
            TextFileDefinition shadowed = candidateCanonical ? existing : candidate;
            definitions[key] = selected;
            diagnostic = $"LFENV11-DROP-ALIAS：采用无首尾空白文件 {Path.GetFileName(selected.SourcePath)}，遮蔽 {Path.GetFileName(shadowed.SourcePath)}。";
            return true;
        }

        private static bool TryMapMonsterContentKey(string relativePath, string directory, out string key)
        {
            key = null;
            string normalized = (relativePath ?? string.Empty).Replace('\\', '/');
            string prefix = directory + "/";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string extension = Path.GetExtension(normalized);
            if (!(extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                  extension.Equals(".ini", StringComparison.OrdinalIgnoreCase))) return false;
            return LogicKey.TryNormalize("MonsterContent/" + directory + "/" +
                                         normalized[prefix.Length..^extension.Length], out key);
        }

        private static bool TryResolveQFunctionAlias(
            string root,
            IDictionary<string, TextFileDefinition> definitions,
            string logicKey,
            TextFileDefinition existing,
            TextFileDefinition candidate)
        {
            if (!logicKey.Equals("systemscripts/qfunction-0", StringComparison.Ordinal)) return false;
            string existingRelative = Path.GetRelativePath(root, existing.SourcePath).Replace('\\', '/');
            string candidateRelative = Path.GetRelativePath(root, candidate.SourcePath).Replace('\\', '/');
            bool existingMarket = existingRelative.Equals(
                "Market_Def/QFunction-0.txt", StringComparison.OrdinalIgnoreCase);
            bool candidateMarket = candidateRelative.Equals(
                "Market_Def/QFunction-0.txt", StringComparison.OrdinalIgnoreCase);
            bool existingRoot = existingRelative.Equals("QFunction-0.txt", StringComparison.OrdinalIgnoreCase);
            bool candidateRoot = candidateRelative.Equals("QFunction-0.txt", StringComparison.OrdinalIgnoreCase);
            if (!((existingMarket && candidateRoot) || (existingRoot && candidateMarket))) return false;
            if (candidateMarket) definitions[logicKey] = candidate;
            return true;
        }

        private static bool IsWithinRoot(string root, string file)
        {
            string rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                       + Path.DirectorySeparatorChar;
            return file.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        public IReadOnlyCollection<TextFileDefinition> GetAll() => _all;

        public TextFileDefinition GetByKey(string key)
        {
            if (!LogicKey.TryNormalize(key, out string normalizedKey)) return null;
            return _definitions.TryGetValue(normalizedKey, out TextFileDefinition definition)
                ? definition
                : null;
        }

        private static bool TryMapLogicKey(TxtScriptLayout layout, string relativePath, out string key)
        {
            key = string.Empty;
            string normalized = relativePath.Replace('\\', '/');
            int separator = normalized.IndexOf('/');
            if (separator <= 0) return false;

            string directory = normalized.Substring(0, separator);
            string nestedPath = normalized.Substring(separator + 1);
            if (layout == TxtScriptLayout.LyoCrystal)
            {
                if (!LyoCrystalAllowedDirectories.Contains(directory)) return false;
                key = LogicKey.NormalizeOrThrow(normalized);
                return true;
            }

            string mappedPath;
            if (directory.Equals("Market_Def", StringComparison.OrdinalIgnoreCase))
                mappedPath = $"NPCs/{nestedPath}";
            else if (directory.Equals("Npc_def", StringComparison.OrdinalIgnoreCase))
                mappedPath = $"NpcDefs/{nestedPath}";
            else if (directory.Equals("QuestDiary", StringComparison.OrdinalIgnoreCase))
                mappedPath = $"QuestDiary/{nestedPath}";
            else if (directory.Equals("DeFines", StringComparison.OrdinalIgnoreCase))
                mappedPath = $"Defines/{nestedPath}";
            else if (directory.Equals("MapQuest_def", StringComparison.OrdinalIgnoreCase)
                     && nestedPath.Equals("QManage.txt", StringComparison.OrdinalIgnoreCase))
                mappedPath = "SystemScripts/QManage";
            else if (directory.Equals("Robot_def", StringComparison.OrdinalIgnoreCase)
                     && nestedPath.Equals("ROBOTMANAGE.txt", StringComparison.OrdinalIgnoreCase))
                mappedPath = "SystemScripts/RobotManage";
            else if (directory.Equals("Robot_def", StringComparison.OrdinalIgnoreCase)
                     && nestedPath.Equals("AUTORUNROBOT.txt", StringComparison.OrdinalIgnoreCase))
                mappedPath = "SystemScripts/AutoRunRobot";
            else
                return false;

            key = LogicKey.NormalizeOrThrow(mappedPath);
            return true;
        }

        private static TextFileDefinition ReadDefinition(string key, string file, long maxFileBytes)
        {
            long fileBytes = new FileInfo(file).Length;
            if (fileBytes > maxFileBytes)
            {
                throw new InvalidDataException(
                    $"TXT 文件超过配置的 {maxFileBytes} 字节上限：{Path.GetFullPath(file)}（实际 {fileBytes} 字节）");
            }

            byte[] bytes = File.ReadAllBytes(file);
            string text;
            string encodingName;
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            try
            {
                int offset = hasBom ? 3 : 0;
                text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
                encodingName = hasBom ? "UTF-8 BOM" : "UTF-8";
            }
            catch (DecoderFallbackException utf8Exception)
            {
                if (hasBom)
                {
                    throw new InvalidDataException(
                        $"声明为 UTF-8 BOM 的 TXT 文件包含无效正文：{Path.GetFullPath(file)}",
                        utf8Exception);
                }

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                try
                {
                    text = Encoding.GetEncoding(936,
                        EncoderFallback.ExceptionFallback,
                        DecoderFallback.ExceptionFallback).GetString(bytes);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new InvalidDataException(
                        $"TXT 文件既不是有效的严格 UTF-8 或 CP936 文本：{Path.GetFullPath(file)}",
                        exception);
                }

                encodingName = "CP936";
            }

            string newLine = DetectNewLine(text);
            string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var definition = new TextFileDefinition(key, Path.GetFullPath(file), encodingName, newLine);
            foreach ((string line, int sourceLine) in BuildLogicalLines(lines))
                definition.AddLine(line, sourceLine);
            return definition;
        }

        private static IEnumerable<(string Line, int SourceLine)> BuildLogicalLines(IReadOnlyList<string> lines)
        {
            for (int index = 0; index < lines.Count; index++)
            {
                int sourceLine = index + 1;
                string logical = lines[index] ?? string.Empty;
                while (HasContinuation(logical) && index + 1 < lines.Count)
                {
                    logical = logical.TrimEnd();
                    logical = logical.Substring(0, logical.Length - 1) + " " +
                              (lines[++index] ?? string.Empty).TrimStart();
                }
                yield return (logical, sourceLine);
            }
        }

        private static bool HasContinuation(string line)
        {
            string value = (line ?? string.Empty).TrimEnd();
            int slashes = 0;
            for (int index = value.Length - 1; index >= 0 && value[index] == '\\'; index--) slashes++;
            return slashes % 2 == 1;
        }

        private static string DetectNewLine(string text)
        {
            bool hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
            bool hasBareLf = text.Replace("\r\n", string.Empty, StringComparison.Ordinal)
                .Contains('\n', StringComparison.Ordinal);
            bool hasBareCr = text.Replace("\r\n", string.Empty, StringComparison.Ordinal)
                .Contains('\r', StringComparison.Ordinal);
            int kinds = (hasCrLf ? 1 : 0) + (hasBareLf ? 1 : 0) + (hasBareCr ? 1 : 0);
            if (kinds > 1) return "MIXED";
            if (hasCrLf) return "CRLF";
            if (hasBareLf) return "LF";
            if (hasBareCr) return "CR";
            return "NONE";
        }
    }
}
