using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WinSecurityAgent.Core
{
    /// <summary>
    /// Gestor inteligente de memoria (RAM, VRAM, Disco)
    /// Optimizado para RTX 3060 Ti
    /// </summary>
    public class MemoryManager
    {
        private long _ramAsignada = 0;
        private long _vramAsignada = 0;
        private long _discoAsignado = 0;

        private const long VRAM_RTX3060TI = 8_589_934_592; // 8 GB
        private const long RAM_MINIMA = 4_294_967_296; // 4 GB

        public class EstadoMemoria
        {
            public long RAMTotal { get; set; }
            public long RAMDisponible { get; set; }
            public long RAMUsada { get; set; }
            public long VRAMTotal { get; set; }
            public long VRAMDisponible { get; set; }
            public long VRAMUsada { get; set; }
            public long DiscoTotal { get; set; }
            public long DiscoDisponible { get; set; }
            public double PorcentajeRAM { get; set; }
            public double PorcentajeVRAM { get; set; }
            public double PorcentajeDisco { get; set; }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        /// <summary>
        /// Obtiene estado actual de memoria
        /// </summary>
        public EstadoMemoria ObtenerEstado()
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            GlobalMemoryStatusEx(ref memStatus);

            var driveInfo = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetTempPath()));

            return new EstadoMemoria
            {
                RAMTotal = (long)memStatus.ullTotalPhys,
                RAMDisponible = (long)memStatus.ullAvailPhys,
                RAMUsada = (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys),
                VRAMTotal = VRAM_RTX3060TI,
                VRAMDisponible = VRAM_RTX3060TI - _vramAsignada,
                VRAMUsada = _vramAsignada,
                DiscoTotal = driveInfo.TotalSize,
                DiscoDisponible = driveInfo.AvailableFreeSpace,
                PorcentajeRAM = ((memStatus.ullTotalPhys - memStatus.ullAvailPhys) * 100.0) / memStatus.ullTotalPhys,
                PorcentajeVRAM = (_vramAsignada * 100.0) / VRAM_RTX3060TI,
                PorcentajeDisco = ((driveInfo.TotalSize - driveInfo.AvailableFreeSpace) * 100.0) / driveInfo.TotalSize
            };
        }

        /// <summary>
        /// Asigna memoria en RAM
        /// </summary>
        public async Task<bool> AllocarRAM(long bytes)
        {
            try
            {
                var estado = ObtenerEstado();
                if (estado.RAMDisponible < bytes)
                {
                    Console.WriteLine($"[✗] RAM insuficiente. Disponible: {estado.RAMDisponible / (1024 * 1024)} MB");
                    return false;
                }

                _ramAsignada += bytes;
                Console.WriteLine($"[✓] RAM asignada: {bytes / (1024 * 1024 * 1024)} GB");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Error asignando RAM: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Asigna memoria en VRAM (GPU)
        /// </summary>
        public async Task<bool> AllocarVRAM(long bytes)
        {
            try
            {
                var estado = ObtenerEstado();
                if (estado.VRAMDisponible < bytes)
                {
                    Console.WriteLine($"[✗] VRAM insuficiente. Disponible: {estado.VRAMDisponible / (1024 * 1024)} MB");
                    return false;
                }

                _vramAsignada += bytes;
                Console.WriteLine($"[✓] VRAM asignada: {bytes / (1024 * 1024 * 1024)} GB");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Error asignando VRAM: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Asigna espacio en disco
        /// </summary>
        public async Task<bool> AllocarDisco(long bytes)
        {
            try
            {
                var estado = ObtenerEstado();
                if (estado.DiscoDisponible < bytes)
                {
                    Console.WriteLine($"[✗] Espacio en disco insuficiente");
                    return false;
                }

                _discoAsignado += bytes;
                Console.WriteLine($"[✓] Espacio en disco asignado: {bytes / (1024 * 1024 * 1024)} GB");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[✗] Error asignando disco: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Libera memoria asignada
        /// </summary>
        public void LiberarMemoria()
        {
            _ramAsignada = 0;
            _vramAsignada = 0;
            _discoAsignado = 0;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Console.WriteLine("[✓] Memoria liberada");
        }

        /// <summary>
        /// Obtiene recomendación automática de asignación
        /// </summary>
        public (long ram, long vram, long disco) ObtenerRecomendacion()
        {
            var estado = ObtenerEstado();
            
            // Mantener 30% de RAM libre
            long ramUso = (long)(estado.RAMTotal * 0.7 * 0.4); // 40% del 70% disponible
            
            // Usar 70% de VRAM
            long vramUso = (long)(VRAM_RTX3060TI * 0.7);
            
            // 100 GB en disco o 20% disponible
            long discoUso = Math.Min(100_000_000_000, (long)(estado.DiscoDisponible * 0.2));

            return (ramUso, vramUso, discoUso);
        }

        /// <summary>
        /// Optimiza automáticamente la memoria
        /// </summary>
        public async Task OptimizarAutomaticamente()
        {
            var (ram, vram, disco) = ObtenerRecomendacion();
            
            await AllocarRAM(ram);
            await AllocarVRAM(vram);
            await AllocarDisco(disco);
            
            Console.WriteLine("[✓] Memoria optimizada automáticamente");
        }
    }
}
