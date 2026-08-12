namespace Server.Scripting
{
    public sealed class NpcPageCall
    {
        public NpcPageCall(
            string npcFileName,
            uint npcObjectID,
            int npcScriptID,
            string pageKey,
            string definitionKey,
            IReadOnlyList<string> args,
            string input)
        {
            NpcFileName = npcFileName ?? string.Empty;
            NpcObjectID = npcObjectID;
            NpcScriptID = npcScriptID;
            PageKey = pageKey ?? string.Empty;
            DefinitionKey = definitionKey ?? string.Empty;
            Args = args ?? Array.Empty<string>();
            Input = input ?? string.Empty;
        }

        public string NpcFileName { get; }
        public uint NpcObjectID { get; }
        public int NpcScriptID { get; }

        /// <summary>
        /// 客户端实际调用的 Key（可能带参数，例如 [@TELEPORT(1,2)]）。
        /// </summary>
        public string PageKey { get; }

        /// <summary>
        /// 用于匹配“页面定义”的 Key（如 [@TELEPORT()]）。
        /// </summary>
        public string DefinitionKey { get; }

        /// <summary>
        /// PageKey 中解析出的参数（与 txt 的 %ARG(n) 机制一致）。
        /// </summary>
        public IReadOnlyList<string> Args { get; }

        /// <summary>
        /// 输入框回填（对应 NPCConfirmInput.Value；若非输入页则为空）。
        /// </summary>
        public string Input { get; }
    }
}

