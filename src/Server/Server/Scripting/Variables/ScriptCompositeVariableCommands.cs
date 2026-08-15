using System.Globalization;

namespace Server.Scripting.Variables
{
    public readonly struct ScriptCompositeResult
    {
        internal ScriptCompositeResult(
            bool success,
            ScriptVariableErrorCode errorCode,
            ScriptVariableValue value,
            int number,
            bool matched,
            string diagnostic)
        {
            Success = success;
            ErrorCode = errorCode;
            Value = value;
            Number = number;
            Matched = matched;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Success { get; }
        public ScriptVariableErrorCode ErrorCode { get; }
        public ScriptVariableValue Value { get; }
        public int Number { get; }
        public bool Matched { get; }
        public string Diagnostic { get; }
    }

    /// <summary>有界 L$/D$ 临时复合变量操作；所有写入最终仍经过统一变量模块。</summary>
    public sealed class ScriptCompositeVariableCommands
    {
        private readonly IScriptVariableModule _module;

        public ScriptCompositeVariableCommands(IScriptVariableModule module) =>
            _module = module ?? throw new ArgumentNullException(nameof(module));

        public bool IsCompositeReference(string text) =>
            TryParseReference(text, out var reference, out _) &&
            (reference.Scope == ScriptVariableScope.L || reference.Scope == ScriptVariableScope.Dict);

        public ScriptVariableMutationResult Mutate(
            in ScriptVariableContext context,
            string referenceText,
            string command,
            string operand)
        {
            if (!TryParseReference(referenceText, out var reference, out var selector))
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "复合变量引用无效。", default);
            ScriptVariableReadResult current = _module.Read(context, reference);
            if (!current.Success)
                return MutationFailure(current.ErrorCode, current.Diagnostic, current.Value);

            ScriptVariableResult next = reference.Scope == ScriptVariableScope.L
                ? MutateList(current.Value, selector, command, operand)
                : MutateDictionary(current.Value, selector, command, operand);
            if (!next.Success)
                return MutationFailure(next.ErrorCode, next.Diagnostic, current.Value);
            return _module.Mutate(context, ScriptVariableMutation.Set(reference, next.Value));
        }

        public ScriptCompositeResult Read(in ScriptVariableContext context, string referenceText)
        {
            if (!TryParseReference(referenceText, out var reference, out var selector))
                return Fail(ScriptVariableErrorCode.UnknownReference, "复合变量引用无效。");
            ScriptVariableReadResult current = _module.Read(context, reference);
            if (!current.Success) return Fail(current.ErrorCode, current.Diagnostic);
            if (selector == null) return Ok(current.Value);

            if (reference.Scope == ScriptVariableScope.L)
            {
                if (!TryResolveIndex(selector, current.Value.List.Count, out int index))
                    return Fail(ScriptVariableErrorCode.InvalidExpression, "列表索引越界或格式无效。");
                return Ok(ScriptVariableValue.FromString(current.Value.List[index]));
            }

            var pair = current.Value.Dictionary.FirstOrDefault(item =>
                string.Equals(item.Key, selector, StringComparison.Ordinal));
            return pair.Key == null
                ? Fail(ScriptVariableErrorCode.UnknownReference, "字典中不存在指定键。")
                : Ok(ScriptVariableValue.FromString(pair.Value));
        }

        public ScriptCompositeResult Count(in ScriptVariableContext context, string referenceText)
        {
            ScriptCompositeResult read = Read(context, referenceText);
            if (!read.Success) return read;
            int count = read.Value.Kind == ScriptVariableKind.List
                ? read.Value.List.Count
                : read.Value.Dictionary.Count;
            return new ScriptCompositeResult(true, ScriptVariableErrorCode.None,
                ScriptVariableValue.FromInteger(count), count, false, string.Empty);
        }

        public ScriptCompositeResult Contains(
            in ScriptVariableContext context,
            string referenceText,
            string value,
            bool dictionaryValues = false,
            bool caseSensitive = true)
        {
            ScriptCompositeResult read = Read(context, referenceText);
            if (!read.Success) return read;
            StringComparison comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            bool matched = read.Value.Kind == ScriptVariableKind.List
                ? read.Value.List.Any(item => string.Equals(item, value, comparison))
                : read.Value.Dictionary.Any(pair => string.Equals(
                    dictionaryValues ? pair.Value : pair.Key, value, comparison));
            return new ScriptCompositeResult(true, ScriptVariableErrorCode.None,
                ScriptVariableValue.FromInteger(matched ? 1 : 0), matched ? 1 : 0, matched, string.Empty);
        }

        public ScriptCompositeResult FindListIndex(
            in ScriptVariableContext context, string referenceText, string value)
        {
            ScriptCompositeResult read = Read(context, referenceText);
            if (!read.Success) return read;
            if (read.Value.Kind != ScriptVariableKind.List)
                return Fail(ScriptVariableErrorCode.TypeMismatch, "该命令只支持列表。");
            int index = read.Value.List.ToList().FindIndex(item => string.Equals(item, value, StringComparison.Ordinal));
            return new ScriptCompositeResult(true, ScriptVariableErrorCode.None,
                ScriptVariableValue.FromInteger(index), index, index >= 0, string.Empty);
        }

        public ScriptCompositeResult AllNumeric(in ScriptVariableContext context, string referenceText)
        {
            ScriptCompositeResult read = Read(context, referenceText);
            if (!read.Success) return read;
            IEnumerable<string> values = read.Value.Kind switch
            {
                ScriptVariableKind.List => read.Value.List,
                ScriptVariableKind.Dictionary => read.Value.Dictionary.Select(pair => pair.Value),
                _ => Array.Empty<string>()
            };
            bool matched = values.All(value => decimal.TryParse(value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out _));
            return new ScriptCompositeResult(true, ScriptVariableErrorCode.None,
                ScriptVariableValue.FromInteger(matched ? 1 : 0), matched ? 1 : 0, matched, string.Empty);
        }

        public ScriptVariableMutationResult InsertList(
            in ScriptVariableContext context, string referenceText, string value, int requestedIndex)
        {
            if (!TryGetList(context, referenceText, out var reference, out var oldValue, out var failure))
                return failure;
            var values = oldValue.List.ToList();
            int index = requestedIndex == -1 ? values.Count : requestedIndex;
            if (index < 0 || index > values.Count)
                return MutationFailure(ScriptVariableErrorCode.InvalidExpression, "列表插入索引越界。", oldValue);
            values.Insert(index, value ?? string.Empty);
            return _module.Mutate(context, ScriptVariableMutation.Set(reference, ScriptVariableValue.FromList(values)));
        }

        public ScriptVariableMutationResult RemoveListByIndex(
            in ScriptVariableContext context, string referenceText, int requestedIndex)
        {
            if (!TryGetList(context, referenceText, out var reference, out var oldValue, out var failure))
                return failure;
            var values = oldValue.List.ToList();
            if (!TryResolveIndex(requestedIndex.ToString(CultureInfo.InvariantCulture), values.Count, out int index))
                return MutationFailure(ScriptVariableErrorCode.InvalidExpression, "列表删除索引越界。", oldValue);
            values.RemoveAt(index);
            return _module.Mutate(context, ScriptVariableMutation.Set(reference, ScriptVariableValue.FromList(values)));
        }

        public ScriptVariableMutationResult RemoveListByContent(
            in ScriptVariableContext context,
            string referenceText,
            string content,
            bool caseSensitive = true)
        {
            if (!TryGetList(context, referenceText, out var reference, out var oldValue, out var failure))
                return failure;
            StringComparison comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            var values = oldValue.List
                .Where(item => !string.Equals(item, content, comparison)).ToList();
            return _module.Mutate(context, ScriptVariableMutation.Set(reference, ScriptVariableValue.FromList(values)));
        }

        public ScriptVariableMutationResult ReverseList(
            in ScriptVariableContext context, string sourceText, string destinationText)
        {
            if (!TryGetList(context, sourceText, out _, out var source, out var failure)) return failure;
            if (!TryParseReference(destinationText, out var destination, out var selector) || selector != null ||
                destination.Scope != ScriptVariableScope.L)
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "列表目标引用无效。", source);
            return _module.Mutate(context, ScriptVariableMutation.Set(
                destination, ScriptVariableValue.FromList(source.List.Reverse())));
        }

        public ScriptVariableMutationResult SortList(
            in ScriptVariableContext context,
            string sourceText,
            string destinationText,
            bool descending,
            bool numeric)
        {
            if (!TryGetList(context, sourceText, out _, out var source, out var failure)) return failure;
            if (!TryParseReference(destinationText, out var destination, out var selector) || selector != null ||
                destination.Scope != ScriptVariableScope.L)
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "列表目标引用无效。", source);

            IEnumerable<string> sorted;
            if (numeric)
            {
                var parsed = new List<(string Text, decimal Number)>();
                foreach (string item in source.List)
                {
                    if (!decimal.TryParse(item, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out decimal number))
                        return MutationFailure(ScriptVariableErrorCode.TypeMismatch,
                            "按数字排序要求所有列表项都是十进制数字。", source);
                    parsed.Add((item, number));
                }
                sorted = descending
                    ? parsed.OrderByDescending(item => item.Number).Select(item => item.Text)
                    : parsed.OrderBy(item => item.Number).Select(item => item.Text);
            }
            else
            {
                sorted = descending
                    ? source.List.OrderByDescending(item => item, StringComparer.Ordinal)
                    : source.List.OrderBy(item => item, StringComparer.Ordinal);
            }
            return _module.Mutate(context, ScriptVariableMutation.Set(destination, ScriptVariableValue.FromList(sorted)));
        }

        public ScriptVariableMutationResult SliceList(
            in ScriptVariableContext context,
            string sourceText,
            string destinationText,
            int start,
            int end,
            int step = 1)
        {
            if (!TryGetList(context, sourceText, out _, out var source, out var failure)) return failure;
            if (!TryParseReference(destinationText, out var destination, out var selector) || selector != null ||
                destination.Scope != ScriptVariableScope.L)
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "列表目标引用无效。", source);
            if (step <= 0 || !TryResolveIndex(start.ToString(CultureInfo.InvariantCulture), source.List.Count, out int first) ||
                !TryResolveIndex(end.ToString(CultureInfo.InvariantCulture), source.List.Count, out int last))
                return MutationFailure(ScriptVariableErrorCode.InvalidExpression, "切片索引或步长无效。", source);

            int direction = first <= last ? 1 : -1;
            var result = new List<string>();
            for (int index = first; direction > 0 ? index <= last : index >= last; index += direction * step)
                result.Add(source.List[index]);
            return _module.Mutate(context, ScriptVariableMutation.Set(destination, ScriptVariableValue.FromList(result)));
        }

        public ScriptCompositeResult NumericExtremum(
            in ScriptVariableContext context, string referenceText, bool maximum)
        {
            ScriptCompositeResult read = Read(context, referenceText);
            if (!read.Success) return read;
            IEnumerable<KeyValuePair<string, string>> values = read.Value.Kind switch
            {
                ScriptVariableKind.List => read.Value.List.Select((value, index) =>
                    new KeyValuePair<string, string>(index.ToString(CultureInfo.InvariantCulture), value)),
                ScriptVariableKind.Dictionary => read.Value.Dictionary,
                _ => Array.Empty<KeyValuePair<string, string>>()
            };
            var parsed = new List<(string Key, decimal Number)>();
            foreach (var pair in values)
            {
                if (!decimal.TryParse(pair.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out decimal number))
                    return Fail(ScriptVariableErrorCode.TypeMismatch, "取极值要求所有值都是十进制数字。");
                parsed.Add((pair.Key, number));
            }
            if (parsed.Count == 0) return Fail(ScriptVariableErrorCode.InvalidExpression, "空集合没有极值。");
            var selected = maximum
                ? parsed.OrderByDescending(item => item.Number).First()
                : parsed.OrderBy(item => item.Number).First();
            return new ScriptCompositeResult(true, ScriptVariableErrorCode.None,
                ScriptVariableValue.FromDecimal(selected.Number), 0, false, selected.Key);
        }

        public ScriptVariableMutationResult DictionaryItems(
            in ScriptVariableContext context,
            string sourceText,
            string destinationText,
            bool values)
        {
            if (!TryParseReference(sourceText, out var sourceReference, out var selector) || selector != null ||
                sourceReference.Scope != ScriptVariableScope.Dict)
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "字典引用无效。", default);
            ScriptVariableReadResult source = _module.Read(context, sourceReference);
            if (!source.Success) return MutationFailure(source.ErrorCode, source.Diagnostic, source.Value);
            if (!TryParseReference(destinationText, out var destination, out selector) || selector != null ||
                destination.Scope != ScriptVariableScope.L)
                return MutationFailure(ScriptVariableErrorCode.UnknownReference, "列表目标引用无效。", source.Value);
            return _module.Mutate(context, ScriptVariableMutation.Set(destination, ScriptVariableValue.FromList(
                source.Value.Dictionary.Select(pair => values ? pair.Value : pair.Key))));
        }

        private bool TryGetList(
            in ScriptVariableContext context,
            string referenceText,
            out ScriptVariableReference reference,
            out ScriptVariableValue value,
            out ScriptVariableMutationResult failure)
        {
            value = default;
            failure = default;
            if (!TryParseReference(referenceText, out reference, out var selector) || selector != null ||
                reference.Scope != ScriptVariableScope.L)
            {
                failure = MutationFailure(ScriptVariableErrorCode.UnknownReference, "列表引用无效。", default);
                return false;
            }
            ScriptVariableReadResult read = _module.Read(context, reference);
            if (read.Success)
            {
                value = read.Value;
                return true;
            }
            failure = MutationFailure(read.ErrorCode, read.Diagnostic, read.Value);
            return false;
        }

        private static ScriptVariableResult MutateList(
            ScriptVariableValue current, string selector, string command, string operand)
        {
            string operation = (command ?? string.Empty).Trim().ToUpperInvariant();
            var values = current.List.ToList();
            if (selector == null)
            {
                if (operation == "MOV")
                    return TryParseListLiteral(operand, out var replacement)
                        ? ScriptVariableResult.Ok(ScriptVariableValue.FromList(replacement))
                        : ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "列表整体赋值必须使用 [值1,值2] 格式。");
                if (operation == "INC") values.Add(operand ?? string.Empty);
                else if (operation == "DEC")
                {
                    int found = values.FindIndex(item => string.Equals(item, operand, StringComparison.Ordinal));
                    if (found >= 0) values.RemoveAt(found);
                }
                else return ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "列表仅支持 MOV/INC/DEC。 ");
            }
            else
            {
                if (!TryResolveIndex(selector, values.Count, out int index))
                    return ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "列表索引越界或格式无效。");
                if (operation == "MOV") values[index] = operand ?? string.Empty;
                else if (operation == "INC") values[index] += operand ?? string.Empty;
                else if (operation == "DEC") values[index] = values[index].Replace(operand ?? string.Empty, string.Empty, StringComparison.Ordinal);
                else return ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "列表元素仅支持 MOV/INC/DEC。 ");
            }
            return ScriptVariableResult.Ok(ScriptVariableValue.FromList(values));
        }

        private static ScriptVariableResult MutateDictionary(
            ScriptVariableValue current, string selector, string command, string operand)
        {
            string operation = (command ?? string.Empty).Trim().ToUpperInvariant();
            var values = current.Dictionary.ToList();
            if (selector == null && operation == "MOV")
                return TryParseDictionaryLiteral(operand, out var replacement)
                    ? ScriptVariableResult.Ok(ScriptVariableValue.FromDictionary(replacement))
                    : ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "字典整体赋值必须使用 {键:值} 格式。");

            string key = selector;
            string value = operand ?? string.Empty;
            if (selector == null && operation == "INC")
            {
                int separator = value.IndexOf(':');
                if (separator <= 0)
                    return ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "字典追加必须使用 键:值 格式。");
                key = value.Substring(0, separator).Trim();
                value = value.Substring(separator + 1).Trim();
            }
            else if (selector == null && operation == "DEC")
                key = value.Trim();
            else if (selector != null && operation != "MOV")
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "字典键赋值仅支持 MOV。");
            else if (selector == null)
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "字典仅支持 MOV/INC/DEC。");

            if (string.IsNullOrEmpty(key))
                return ScriptVariableResult.Fail(ScriptVariableErrorCode.InvalidExpression, "字典键不能为空。");
            int index = values.FindIndex(pair => string.Equals(pair.Key, key, StringComparison.Ordinal));
            if (operation == "DEC")
            {
                if (index >= 0) values.RemoveAt(index);
            }
            else if (index >= 0) values[index] = new KeyValuePair<string, string>(key, value);
            else values.Add(new KeyValuePair<string, string>(key, value));
            return ScriptVariableResult.Ok(ScriptVariableValue.FromDictionary(values));
        }

        private static bool TryParseListLiteral(string text, out IReadOnlyList<string> values)
        {
            values = Array.Empty<string>();
            string source = (text ?? string.Empty).Trim();
            if (source.Length < 2 || source[0] != '[' || source[^1] != ']') return false;
            string body = source.Substring(1, source.Length - 2);
            values = body.Length == 0
                ? Array.Empty<string>()
                : body.Split(',').Select(item => item.Trim()).ToArray();
            return true;
        }

        private static bool TryParseDictionaryLiteral(
            string text, out IReadOnlyList<KeyValuePair<string, string>> values)
        {
            values = Array.Empty<KeyValuePair<string, string>>();
            string source = (text ?? string.Empty).Trim();
            if (source.Length < 2 || source[0] != '{' || source[^1] != '}') return false;
            var parsed = new List<KeyValuePair<string, string>>();
            string body = source.Substring(1, source.Length - 2);
            if (body.Length == 0) { values = parsed; return true; }
            foreach (string entry in body.Split(','))
            {
                int separator = entry.IndexOf(':');
                if (separator <= 0) return false;
                string key = entry.Substring(0, separator).Trim();
                if (key.Length == 0 || parsed.Any(pair => string.Equals(pair.Key, key, StringComparison.Ordinal))) return false;
                parsed.Add(new KeyValuePair<string, string>(key, entry.Substring(separator + 1).Trim()));
            }
            values = parsed;
            return true;
        }

        private static bool TryParseReference(
            string text, out ScriptVariableReference reference, out string selector)
        {
            reference = default;
            selector = null;
            string source = (text ?? string.Empty).Trim();
            int open = source.LastIndexOf('[');
            if (open >= 0 && source.EndsWith("]", StringComparison.Ordinal))
            {
                selector = source.Substring(open + 1, source.Length - open - 2);
                source = source.Substring(0, open);
            }
            return ScriptVariableReferenceParser.TryParse(source, out reference) &&
                   (reference.Scope == ScriptVariableScope.L || reference.Scope == ScriptVariableScope.Dict);
        }

        private static bool TryResolveIndex(string text, int count, out int index)
        {
            index = -1;
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int requested))
                return false;
            index = requested < 0 ? count + requested : requested;
            return index >= 0 && index < count;
        }

        private static ScriptCompositeResult Ok(ScriptVariableValue value) =>
            new ScriptCompositeResult(true, ScriptVariableErrorCode.None, value, 0, false, string.Empty);

        private static ScriptCompositeResult Fail(ScriptVariableErrorCode code, string diagnostic) =>
            new ScriptCompositeResult(false, code, default, 0, false, diagnostic);

        private static ScriptVariableMutationResult MutationFailure(
            ScriptVariableErrorCode code, string diagnostic, ScriptVariableValue oldValue) =>
            new ScriptVariableMutationResult(false, code, oldValue, oldValue, diagnostic);
    }
}
