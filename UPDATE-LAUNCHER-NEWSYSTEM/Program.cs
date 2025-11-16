using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;

/*
 * =================================================================================
 * LAUNCHER UPDATER
 * =================================================================================
 * Este executável é chamado pelo Launcher principal (PDV_Laucher.exe).
 * Seu único trabalho é:
 * 1. Esperar o Launcher principal fechar.
 * 2. Mover o .exe principal para .exe.old.
 * 3. Extrair o novo .zip por cima.
 * 4. Em caso de falha, reverter (mover .old de volta para .exe).
 * 5. Iniciar o novo .exe.
 * =================================================================================
 */

namespace LauncherUpdater
{
    internal class Program
    {
        static int Main(string[] args)
        {
            // Argumentos esperados:
            // 0: --pid [ID_DO_PROCESSO_PAI]
            // 1: --zip-path [CAMINHO_DO_ZIP_BAIXADO]
            // 2: --target-dir [PASTA_DO_LAUNCHER_INSTALADO]
            // 3: --exe-name [NOME_DO_EXE_PRINCIPAL]

            try
            {
                // 1. Parse dos argumentos
                var argsDict = args.Select(s => s.Split(new[] { '=' }, 2))
                                   .ToDictionary(a => a[0], a => a[1]);

                int parentPid = int.Parse(argsDict["--pid"]);
                string zipPath = argsDict["--zip-path"];
                string targetDir = argsDict["--target-dir"];
                string exeName = argsDict["--exe-name"];

                string targetExePath = Path.Combine(targetDir, exeName);
                string oldExePath = targetExePath + ".old";

                // 2. Esperar o processo pai (Launcher) fechar
                try
                {
                    Process parentProcess = Process.GetProcessById(parentPid);
                    Console.WriteLine($"Aguardando o processo principal (PID: {parentPid}) fechar...");
                    parentProcess.WaitForExit(10000); // Espera até 10 segundos
                }
                catch (ArgumentException)
                {
                    // Processo já fechou, ótimo.
                }

                // Pausa de segurança (como no .bat)
                Thread.Sleep(3000);

                // 3. Tentar a atualização (Lógica "8 ou 80")
                try
                {
                    // 3.1 Mover o .exe atual para .old
                    if (File.Exists(targetExePath))
                    {
                        Console.WriteLine($"Renomeando {exeName} para {exeName}.old");
                        File.Move(targetExePath, oldExePath, true);
                    }

                    // 3.2 Extrair o novo .zip
                    Console.WriteLine($"Extraindo {zipPath} para {targetDir}...");
                    ZipFile.ExtractToDirectory(zipPath, targetDir, true);

                    // 3.3 Limpar o .old (Sucesso!)
                    if (File.Exists(oldExePath))
                    {
                        File.Delete(oldExePath);
                    }
                    Console.WriteLine("Atualização concluída com sucesso!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERRO CRÍTICO NA ATUALIZAÇÃO: {ex.Message}");
                    // 4. ROLLBACK! Se algo deu errado, reverte.
                    if (File.Exists(oldExePath) && !File.Exists(targetExePath))
                    {
                        Console.WriteLine("Tentando reverter para a versão anterior...");
                        File.Move(oldExePath, targetExePath, true);
                        Console.WriteLine("Reversão concluída.");
                    }
                    else
                    {
                        Console.WriteLine("Não foi possível reverter automaticamente.");
                    }
                }
                finally
                {
                    // 5. Tentar reiniciar o Launcher (seja a versão nova ou a antiga revertida)
                    if (File.Exists(targetExePath))
                    {
                        Console.WriteLine("Reiniciando o Launcher...");
                        Process.Start(new ProcessStartInfo(targetExePath)
                        {
                            UseShellExecute = true // Necessário para .exe
                        });
                    }
                    else
                    {
                        Console.WriteLine($"ERRO FATAL: {targetExePath} não encontrado. Não foi possível reiniciar.");
                    }
                }

                return 0; // Sucesso
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERRO INESPERADO NO ATUALIZADOR: {ex.Message}");
                Console.WriteLine("Pressione Enter para sair...");
                Console.ReadLine();
                return 1; // Falha
            }
        }
    }
}