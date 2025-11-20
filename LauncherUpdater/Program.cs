using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows;

namespace LauncherUpdater
{
    internal class Program
    {
        const string NOME_ATALHO = "PDV NewSystem";

        static string _caminhoLogDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RELATORIO_UPDATER.txt");
        static string _exeNameSolicitado = "PDV_Launcher.exe";

        static void Main(string[] args)
        {
            try
            {
                // Limpa o log antigo para começar uma nova história
                File.WriteAllText(_caminhoLogDesktop, $"--- INICIO DA NARRAÇÃO: {DateTime.Now} ---\n");
                Narrar("O Updater acordou! Estou rodando.");
                Narrar($"Recebi {args.Length} argumentos: {string.Join(" ", args)}");

                if (args.Length == 0)
                {
                    Narrar("ERRO: Não recebi nenhum argumento! Não sei o que fazer. Vou dormir.");
                    return;
                }

                if (args.Contains("--fix-mode"))
                {
                    Narrar("Modo de correção de nome detectado.");
                    ExecutarCorrecaoNome(args);
                    return;
                }

                ExecutarAtualizacaoPadrao(args);
            }
            catch (Exception ex)
            {
                Narrar("\n------------------------------------------------");
                Narrar("!!! ERRO CATASTRÓFICO NÃO TRATADO !!!");
                Narrar(ex.ToString());
                Narrar("------------------------------------------------");
            }
            finally
            {
                Narrar($"\nFim da execução em {DateTime.Now}.");
            }
        }

        private static void ExecutarAtualizacaoPadrao(string[] args)
        {
            // 1. PARSING E VARIAVEIS
            Narrar("Lendo argumentos...");
            int parentPid = int.Parse(GetArg(args, "--pid"));
            string zipPath = GetArg(args, "--zip-path");
            string targetDir = GetArg(args, "--target-dir");
            _exeNameSolicitado = GetArg(args, "--exe-name"); // Atualiza a variável estática

            Narrar($"PID do Pai (Launcher): {parentPid}");
            Narrar($"Arquivo ZIP baixado: {zipPath}");
            Narrar($"Pasta de Destino (Onde vou instalar): {targetDir}");
            Narrar($"Executável para reabrir: {_exeNameSolicitado}");

            if (!File.Exists(zipPath))
            {
                Narrar("ERRO CRÍTICO: O arquivo ZIP não existe no caminho informado! Abortando.");
                return;
            }
            if (!Directory.Exists(targetDir))
            {
                Narrar("AVISO: A pasta de destino não existe. Vou tentar criar.");
            }

            Narrar("Passo 1: Garantir que ninguém está usando os arquivos...");

            // Tenta esperar o processo pai fechar
            if (parentPid > 0)
            {
                try
                {
                    Process p = Process.GetProcessById(parentPid);
                    Narrar($"O processo pai ({parentPid}) ainda está vivo. Esperando ele fechar...");
                    p.WaitForExit(5000);
                    if (!p.HasExited)
                    {
                        Narrar("Ele não quis fechar por bem. Vou matar ele agora.");
                        p.Kill();
                    }
                    else
                    {
                        Narrar("O processo pai fechou pacificamente.");
                    }
                }
                catch (Exception ex)
                {
                    Narrar($"O processo pai já sumiu ou não consegui acessar: {ex.Message}");
                }
            }

            Narrar("Procurando processos zumbis travando a pasta...");
            MatarProcessosNaPasta(targetDir);

            string tempFolder = Path.Combine(Path.GetTempPath(), "PDV_Extracted_" + Guid.NewGuid().ToString().Substring(0, 5));
            Narrar($"Passo 2: Extraindo arquivos para pasta temporária: {tempFolder}");

            try
            {
                Directory.CreateDirectory(tempFolder);
                ZipFile.ExtractToDirectory(zipPath, tempFolder, true);
                Narrar("Extração do ZIP concluída com sucesso.");
            }
            catch (Exception ex)
            {
                Narrar($"ERRO NA EXTRAÇÃO: {ex.Message}");
                throw;
            }

            Narrar("Passo 3: Movendo arquivos para a pasta real (com tentativas)...");
            string origemReal = tempFolder;

            var dirs = Directory.GetDirectories(tempFolder);
            var files = Directory.GetFiles(tempFolder);
            if (files.Length == 0 && dirs.Length == 1)
            {
                Narrar($"Notei que o ZIP tinha uma pasta dentro ('{new DirectoryInfo(dirs[0]).Name}'). Entrando nela.");
                origemReal = dirs[0];
            }

            CopiarRecursivo(origemReal, targetDir);

            Narrar("Passo 4: Limpando a bagunça...");
            try
            {
                if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
                if (File.Exists(zipPath)) File.Delete(zipPath);
                Narrar("Arquivos temporários apagados.");
            }
            catch (Exception ex) { Narrar($"Falha na limpeza (não crítico): {ex.Message}"); }

            string caminhoExeFinal = Path.Combine(targetDir, _exeNameSolicitado);
            Narrar($"Passo 5: Tentando reabrir o executável em: {caminhoExeFinal}");

            if (File.Exists(caminhoExeFinal))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(caminhoExeFinal)
                    {
                        WorkingDirectory = targetDir,
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    Narrar("SUCESSO: Executável reiniciado! Meu trabalho aqui acabou.");
                }
                catch (Exception ex)
                {
                    Narrar($"ERRO AO REINICIAR: {ex.Message}");
                }
            }
            else
            {
                Narrar("ERRO CRÍTICO: O executável final SUMIU!");
            }
        }

        static void Narrar(string texto)
        {
            try
            {
                string linha = $"[{DateTime.Now:HH:mm:ss}] {texto}\n";
                File.AppendAllText(_caminhoLogDesktop, linha);
                Console.WriteLine(texto);
            }
            catch { /* Se não der pra escrever o log, não tem o que fazer */ }
        }

        static string GetArg(string[] args, string key)
        {
            foreach (var arg in args)
            {
                string a = arg.TrimStart('-');
                string k = key.TrimStart('-');
                if (a.StartsWith(k + "="))
                    return a.Split(new[] { '=' }, 2)[1].Trim('"');
            }
            return "";
        }

        static void MatarProcessosNaPasta(string dir)
        {
            try
            {
                var todosProcessos = Process.GetProcesses();
                foreach (var p in todosProcessos)
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue; // Não me mata!
                        // Verifica se o caminho principal do módulo do processo começa com a pasta de destino
                        if (p.MainModule != null && p.MainModule.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                        {
                            Narrar($" -> Matando processo travado: {p.ProcessName} (PID: {p.Id})");
                            p.Kill();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Narrar($"Erro ao buscar processos travados: {ex.Message}"); }
        }

        static void CopiarRecursivo(string origem, string destino)
        {
            // Cria destino se não existir
            Directory.CreateDirectory(destino);

            // Copia Arquivos
            foreach (var arquivo in Directory.GetFiles(origem))
            {
                string nomeArquivo = Path.GetFileName(arquivo);
                string destinoArquivo = Path.Combine(destino, nomeArquivo);

                Narrar($"   -> Copiando: {nomeArquivo}");

                bool copiou = false;
                for (int i = 1; i <= 5; i++) // 5 Tentativas
                {
                    try
                    {
                        File.Copy(arquivo, destinoArquivo, true);
                        copiou = true;
                        break;
                    }
                    catch (IOException ioEx)
                    {
                        Narrar($"      [Tentativa {i}] Arquivo preso! ({ioEx.Message}). Esperando...");
                        Thread.Sleep(1000);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Narrar($"      [Tentativa {i}] Sem permissão! Tentando liberar acesso...");
                        try { File.SetAttributes(destinoArquivo, FileAttributes.Normal); } catch { }
                        Thread.Sleep(500);
                    }
                }

                if (!copiou)
                {
                    Narrar($"      XXX FALHA CRÍTICA: Desisti de copiar {nomeArquivo} após 5 tentativas.");
                    throw new Exception($"Falha ao copiar {nomeArquivo}");
                }
            }

            // Copia Subpastas
            foreach (var dir in Directory.GetDirectories(origem))
            {
                string nomeDir = new DirectoryInfo(dir).Name;
                string destinoDir = Path.Combine(destino, nomeDir);
                CopiarRecursivo(dir, destinoDir);
            }
        }

        // Mantido para compatibilidade, mas vazio por enquanto
        private static void ExecutarCorrecaoNome(string[] args) { Narrar("Executando correção de nome (dummy)..."); }
    }
}