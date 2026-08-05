using System.Net;
using Server.MirEnvir;
using System.Collections.Concurrent;
using System.Collections.Generic;
using S = ServerPackets;

namespace Server.Library.Utils
{
    class HttpServer : HttpService
    {
        Thread _thread;
        CancellationTokenSource tokenSource = new();
        private static readonly char[] PathSplitChars = new[] { '/' };
        private const int MicroFileCopyBufferBytes = 256 * 1024;
        private static readonly ConcurrentDictionary<string, CachedSoundList> SoundListCache =
            new(StringComparer.OrdinalIgnoreCase);

        private sealed class CachedSoundList
        {
            public DateTime LastWriteTimeUtc;
            public long FileLength;
            public Dictionary<int, string> Entries = new();
        }

        public HttpServer()
        {
            Host = Settings.HTTPIPAddress;
        }

        public void Start()
        {
            _thread = new Thread(Listen);
            _thread.Start(tokenSource.Token);
        }

        public new void Stop()
        {
            base.Stop();
            
            tokenSource.Cancel();
            Thread.Sleep(1000);
            tokenSource.Dispose();

        }


        public override void OnGetRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var path = request.Url?.AbsolutePath ?? "/";

                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    HandleMicroApi(request, response, path);
                    return;
                }

                if (!IsTrustedClient(request, response))
                    return;

                switch (path.ToLowerInvariant())
                {
                    case "/":
                        WriteResponse(response, GameLanguage.GameName);
                        break;
                    case "/newaccount":
                        var id = request.QueryString["id"];
                        var psd = request.QueryString["psd"];
                        var email = request.QueryString["email"];
                        var name = request.QueryString["name"];
                        var question = request.QueryString["question"];
                        var answer = request.QueryString["answer"];
                        var ip = request.QueryString["ip"];
                        var p = new ClientPackets.NewAccount();
                        p.AccountID = id;
                        p.Password = psd;
                        p.EMailAddress = email;
                        p.UserName = name;
                        p.SecretQuestion = question;
                        p.SecretAnswer = answer;
                        var result = Envir.Main.HTTPNewAccount(p, ip);
                        WriteResponse(response, result.ToString());
                        break;                               
                    case "/addnamelist":
                        id = request.QueryString["id"];
                        var fileName = request.QueryString["fileName"];
                        AddNameList(id, fileName);
                        WriteResponse(response, "true");
                        break;              
                    case "/broadcast":
                        var msg = request.QueryString["msg"];
                        if (msg.Length < 5)
                        {
                            WriteResponse(response, "short");
                            return;
                        }
                        Envir.Main.Broadcast(new S.Chat
                        {
                            Message = msg.Trim(),
                            Type = ChatType.Shout2
                        });
                        WriteResponse(response, "true");
                        break;
                    default:
                        WriteResponse(response, "error");
                        break;
                }
            }
            catch (Exception error)
            {
                try
                {
                    MessageQueue.Instance.Enqueue("Http GET请求处理异常: " + error);
                }
                catch
                {
                }

                AppendRuntimeLog("Http GET request error: " + error.Message);

                if (!HasResponseStarted(response))
                    WriteStatusResponse(response, HttpStatusCode.InternalServerError, "request error: " + error.Message);
            }
        }

        private bool IsTrustedClient(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.RemoteEndPoint == null)
                return true;

            var clientIp = request.RemoteEndPoint.Address.ToString();
            if (clientIp == Settings.HTTPTrustedIPAddress)
                return true;

            WriteStatusResponse(response, HttpStatusCode.Forbidden, "notrusted:" + clientIp);
            return false;
        }

        private void HandleMicroApi(HttpListenerRequest request, HttpListenerResponse response, string absolutePath)
        {
            if (!Settings.MicroServerActive)
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "micro disabled");
                return;
            }

            if (absolutePath.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
            {
                WriteResponse(response, "ok");
                return;
            }

            if (!AuthorizeMicroRequest(request, response))
                return;

            var segments = absolutePath.Split(PathSplitChars, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            var action = segments[1].ToLowerInvariant();
            switch (action)
            {
                case "file":
                    HandleMicroFile(request, response, segments);
                    return;
                case "sound":
                    HandleMicroSound(response, segments);
                    return;
                case "libheader":
                    HandleMicroLibraryHeader(response, segments);
                    return;
                case "libimage":
                    HandleMicroLibraryImage(response, segments);
                    return;
                default:
                    WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                    return;
            }
        }

        private bool AuthorizeMicroRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (string.IsNullOrWhiteSpace(Settings.MicroAuthor))
            {
                WriteStatusResponse(response, HttpStatusCode.ServiceUnavailable, "MicroAuthor not configured");
                return false;
            }

            var user = request.Headers["User"];
            var code = request.Headers["Code"];

            if (!string.Equals(user, Settings.MicroAuthor, StringComparison.Ordinal))
            {
                WriteStatusResponse(response, HttpStatusCode.Unauthorized, "unauthorized");
                return false;
            }

            if (!string.IsNullOrEmpty(Settings.MicroCode) && !string.Equals(code, Settings.MicroCode, StringComparison.Ordinal))
            {
                WriteStatusResponse(response, HttpStatusCode.Unauthorized, "unauthorized");
                return false;
            }

            return true;
        }

        private static bool TryResolveMicroResourceFile(string encodedPath, string encodedName, out string fullPath)
        {
            fullPath = null;

            if (string.IsNullOrWhiteSpace(Settings.MicroResourcePath))
                return false;

            var root = Path.GetFullPath(Settings.MicroResourcePath);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;

            var decodedPath = WebUtility.UrlDecode(encodedPath ?? string.Empty) ?? string.Empty;
            decodedPath = decodedPath.Replace('_', Path.DirectorySeparatorChar)
                                     .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var decodedName = WebUtility.UrlDecode(encodedName ?? string.Empty) ?? string.Empty;
            decodedName = decodedName.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (decodedName.Length == 0)
                return false;

            var combined = Path.GetFullPath(Path.Combine(root, decodedPath, decodedName));
            if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;

            fullPath = combined;
            return true;
        }

        private void HandleMicroFile(HttpListenerRequest request, HttpListenerResponse response, string[] segments)
        {
            if (segments.Length != 4)
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            if (!TryResolveMicroResourceFile(segments[2], segments[3], out var fullPath) || !File.Exists(fullPath))
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            try
            {
                using var fileStream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: MicroFileCopyBufferBytes,
                    options: FileOptions.SequentialScan);
                response.AddHeader("Accept-Ranges", "bytes");

                var totalLength = fileStream.Length;
                var rangeHeader = request?.Headers["Range"];
                if (!string.IsNullOrWhiteSpace(rangeHeader) &&
                    TryParseSingleByteRange(rangeHeader, totalLength, out var start, out var end))
                {
                    var length = end - start + 1;

                    response.AddHeader("Content-Range", $"bytes {start}-{end}/{totalLength}");
                    if (!TryBeginStreamingResponse(response, HttpStatusCode.PartialContent, length, "application/octet-stream"))
                        return;

                    fileStream.Seek(start, SeekOrigin.Begin);
                    CopyStreamRange(fileStream, response.OutputStream, length);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(rangeHeader))
                {
                    response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                    response.AddHeader("Content-Range", $"bytes */{totalLength}");
                    return;
                }

                if (!TryBeginStreamingResponse(response, HttpStatusCode.OK, totalLength, "application/octet-stream"))
                    return;

                CopyStreamRange(fileStream, response.OutputStream, totalLength);
            }
            catch (Exception error)
            {
                if (IsClientDisconnectError(error))
                {
                    AppendRuntimeLog($"Micro file client disconnected: file={fullPath} err={error.Message}");
                    return;
                }

                AppendRuntimeLog($"Micro file response error: file={fullPath} err={error.Message}");
                throw;
            }
        }

        private static bool TryParseSingleByteRange(string rangeHeader, long totalLength, out long start, out long end)
        {
            start = 0;
            end = 0;

            if (string.IsNullOrWhiteSpace(rangeHeader) || totalLength <= 0)
                return false;

            var header = rangeHeader.Trim();
            if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                return false;

            var spec = header.Substring("bytes=".Length).Trim();
            if (spec.Length == 0 || spec.Contains(","))
                return false;

            var dashIndex = spec.IndexOf('-');
            if (dashIndex < 0)
                return false;

            var startPart = spec.Substring(0, dashIndex).Trim();
            var endPart = spec.Substring(dashIndex + 1).Trim();

            if (startPart.Length == 0)
            {
                if (!long.TryParse(endPart, out var suffixLength) || suffixLength <= 0)
                    return false;

                if (suffixLength > totalLength)
                    suffixLength = totalLength;

                start = totalLength - suffixLength;
                end = totalLength - 1;
                return true;
            }

            if (!long.TryParse(startPart, out start) || start < 0 || start >= totalLength)
                return false;

            if (endPart.Length == 0)
            {
                end = totalLength - 1;
                return true;
            }

            if (!long.TryParse(endPart, out end) || end < start)
                return false;

            if (end >= totalLength)
                end = totalLength - 1;

            return true;
        }

        private static void CopyStreamRange(Stream source, Stream destination, long length)
        {
            if (length <= 0)
                return;

            var buffer = new byte[MicroFileCopyBufferBytes];
            var remaining = length;
            var pendingFlushBytes = 0;

            while (remaining > 0)
            {
                var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read <= 0)
                    break;

                destination.Write(buffer, 0, read);
                remaining -= read;
                pendingFlushBytes += read;

                if (pendingFlushBytes >= MicroFileCopyBufferBytes)
                {
                    destination.Flush();
                    pendingFlushBytes = 0;
                }
            }

            if (pendingFlushBytes > 0)
                destination.Flush();
        }

        private static bool IsClientDisconnectError(Exception error)
        {
            if (error == null)
                return false;

            if (error is HttpListenerException listenerException &&
                listenerException.ErrorCode is 64 or 995 or 1229 or 1236)
                return true;

            if (error is System.Net.Sockets.SocketException socketException &&
                socketException.SocketErrorCode is System.Net.Sockets.SocketError.ConnectionAborted or
                    System.Net.Sockets.SocketError.ConnectionReset or
                    System.Net.Sockets.SocketError.OperationAborted or
                    System.Net.Sockets.SocketError.NetworkDown or
                    System.Net.Sockets.SocketError.NetworkReset or
                    System.Net.Sockets.SocketError.Shutdown)
            {
                return true;
            }

            if (error is IOException ioException && IsClientDisconnectError(ioException.InnerException))
                return true;

            int win32Code = error.HResult & 0xFFFF;
            if (win32Code is 64 or 995 or 1229 or 1236)
                return true;

            string message = error.Message ?? string.Empty;
            return message.Contains("网络名不再可用", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("connection was aborted", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("connection reset", StringComparison.OrdinalIgnoreCase);
        }

        private void HandleMicroLibraryHeader(HttpListenerResponse response, string[] segments)
        {
            if (segments.Length != 4)
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            if (!TryResolveMicroResourceFile(segments[2], segments[3], out var fullPath) || !File.Exists(fullPath))
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            var payload = MicroLibraryReader.TryCreateHeaderPayload(fullPath);
            if (payload == null)
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            WriteBytesResponse(response, payload, "application/octet-stream");
        }

        private sealed class MicroSoundResponse
        {
            public byte[] Bytes { get; set; }
            public int Max { get; set; }
            public int Current { get; set; }
        }

        private static bool TryResolveMicroSoundFile(string requestedName, out string fullPath)
        {
            fullPath = null;

            var safeName = Path.GetFileNameWithoutExtension(
                WebUtility.UrlDecode(requestedName ?? string.Empty) ?? string.Empty);
            safeName = (safeName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(safeName))
                return false;

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddMicroSoundCandidate(candidates, safeName + ".wav");

            if (int.TryParse(safeName, out var numericIndex))
                AddMicroSoundCandidatesByIndex(candidates, numericIndex);

            if (TryParseDashedSoundIndex(safeName, out var dashedIndex))
            {
                AddMicroSoundCandidatesByIndex(candidates, dashedIndex);

                if (dashedIndex > 0)
                    AddMicroSoundCandidate(candidates, dashedIndex + ".wav");
            }

            foreach (var candidate in candidates)
            {
                if (!TryResolveMicroResourceFile("Sound", candidate, out var candidatePath))
                    continue;

                if (!File.Exists(candidatePath))
                    continue;

                fullPath = candidatePath;
                return true;
            }

            return false;
        }

        private static void AddMicroSoundCandidatesByIndex(HashSet<string> candidates, int index)
        {
            if (candidates == null || index <= 0)
                return;

            if (TryResolveSoundListAliasFileName(index, out var aliasFileName))
                AddMicroSoundCandidate(candidates, aliasFileName);
        }

        private static void AddMicroSoundCandidate(HashSet<string> candidates, string fileName)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(fileName))
                return;

            var safeFileName = Path.GetFileName(fileName.Trim());
            if (string.IsNullOrWhiteSpace(safeFileName))
                return;

            if (!safeFileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                safeFileName += ".wav";

            candidates.Add(safeFileName);
        }

        private static bool TryParseDashedSoundIndex(string soundName, out int index)
        {
            index = 0;
            if (string.IsNullOrWhiteSpace(soundName))
                return false;

            var parts = soundName.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return false;

            if (!int.TryParse(parts[0], out var prefix) || prefix < 0)
                return false;

            if (!int.TryParse(parts[1], out var suffix) || suffix < 0 || suffix > 9)
                return false;

            try
            {
                index = checked(prefix * 10 + suffix);
                return true;
            }
            catch
            {
                index = 0;
                return false;
            }
        }

        private static bool TryResolveSoundListAliasFileName(int index, out string fileName)
        {
            fileName = string.Empty;

            if (index <= 0)
                return false;

            var entries = GetSoundListEntries();
            if (entries == null)
                return false;

            return entries.TryGetValue(index, out fileName) && !string.IsNullOrWhiteSpace(fileName);
        }

        private static Dictionary<int, string> GetSoundListEntries()
        {
            if (!TryResolveMicroResourceFile("Sound", "SoundList.lst", out var soundListPath))
                return null;

            try
            {
                var info = new FileInfo(soundListPath);
                if (!info.Exists)
                    return null;

                if (SoundListCache.TryGetValue(soundListPath, out var cached)
                    && cached.FileLength == info.Length
                    && cached.LastWriteTimeUtc == info.LastWriteTimeUtc)
                {
                    return cached.Entries;
                }

                var loadedEntries = new Dictionary<int, string>();
                foreach (var line in File.ReadAllLines(soundListPath))
                {
                    var split = line.Replace(" ", string.Empty).Split(':', '\t');
                    if (split.Length <= 1 || !int.TryParse(split[0], out var parsedIndex))
                        continue;

                    var mappedFileName = Path.GetFileName(split[split.Length - 1]);
                    if (string.IsNullOrWhiteSpace(mappedFileName))
                        continue;

                    if (!loadedEntries.ContainsKey(parsedIndex))
                        loadedEntries.Add(parsedIndex, mappedFileName.Trim());
                }

                var loaded = new CachedSoundList
                {
                    FileLength = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Entries = loadedEntries,
                };

                SoundListCache[soundListPath] = loaded;
                return loaded.Entries;
            }
            catch
            {
                return null;
            }
        }

        private void HandleMicroSound(HttpListenerResponse response, string[] segments)
        {
            if (segments.Length != 3 && segments.Length != 4)
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            var name = WebUtility.UrlDecode(segments[2] ?? string.Empty) ?? string.Empty;
            name = Path.GetFileNameWithoutExtension(name);

            var index = 1;
            if (segments.Length == 4 && (!int.TryParse(segments[3], out index) || index < 1))
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            if (!TryResolveMicroSoundFile(name, out var fullPath))
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            const int chunkSize = 1024 * 1024;

            byte[] chunkBytes;
            int maxChunks;

            try
            {
                var fileInfo = new FileInfo(fullPath);
                var totalLength = fileInfo.Length;
                if (totalLength <= 0)
                {
                    WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                    return;
                }

                maxChunks = (int)((totalLength + chunkSize - 1) / chunkSize);
                if (index > maxChunks)
                {
                    WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                    return;
                }

                var offset = (long)(index - 1) * chunkSize;
                var readLength = (int)Math.Min(chunkSize, totalLength - offset);

                chunkBytes = new byte[readLength];
                using var fileStream = File.OpenRead(fullPath);
                fileStream.Seek(offset, SeekOrigin.Begin);
                var read = fileStream.Read(chunkBytes, 0, readLength);
                if (read != readLength)
                {
                    WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                    return;
                }
            }
            catch
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            var payloadObj = new MicroSoundResponse
            {
                Bytes = chunkBytes,
                Max = maxChunks,
                Current = index,
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payloadObj);
            var payload = System.Text.Encoding.UTF8.GetBytes(json);
            WriteStatusBytesResponse(response, HttpStatusCode.OK, payload, "application/json; charset=UTF-8");
        }

        private void HandleMicroLibraryImage(HttpListenerResponse response, string[] segments)
        {
            if (segments.Length != 5 || !int.TryParse(segments[4], out var index))
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            if (!TryResolveMicroResourceFile(segments[2], segments[3], out var fullPath) || !File.Exists(fullPath))
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            var payload = MicroLibraryReader.TryCreateImagePayload(fullPath, index);
            if (payload == null)
            {
                WriteStatusResponse(response, HttpStatusCode.NotFound, "not found");
                return;
            }

            WriteBytesResponse(response, payload, "application/octet-stream");
        }

        void AddNameList(string playerName, string fileName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return;
            if (string.IsNullOrWhiteSpace(fileName)) return;

            Envir.Main.AddNameToNameList(fileName, playerName);
        }    

        public override void OnPostRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            Console.WriteLine("POST request: {0}", request.Url);
        }
    }

}
