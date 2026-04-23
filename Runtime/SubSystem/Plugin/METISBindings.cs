using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace VividRP.Runtime.GPUDriven.METIS
{
    public static unsafe class METISBindings
    {
        public const int METIS_NOPTIONS = 40;

        private const string DllName = "metis";
        private const CharSet DllCharSet = CharSet.Auto;
        private const CallingConvention DllCallingConvention = CallingConvention.Cdecl;

        [DllImport(DllName, CharSet = DllCharSet, CallingConvention = DllCallingConvention)]
        public static extern METISStatus METIS_PartGraphKway(int* nvtxs, int* ncon, int* xadj, int* adjncy, int* vwgt, int* vsize, int* adjwgt,
            int* nparts, float* tpwgts, float* ubvec, METISOptions* options, int* edgecut, int* part);
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum METISStatus
    {
        METIS_OK = 1,
        METIS_ERROR_INPUT = -2,
        METIS_ERROR_MEMORY = -3,
        METIS_ERROR = -4,
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public enum METISOptions
    {
        METIS_OPTION_PTYPE,
        METIS_OPTION_OBJTYPE,
        METIS_OPTION_CTYPE,
        METIS_OPTION_IPTYPE,
        METIS_OPTION_RTYPE,
        METIS_OPTION_DBGLVL,
        METIS_OPTION_NIPARTS,
        METIS_OPTION_NITER,
        METIS_OPTION_NCUTS,
        METIS_OPTION_SEED,
        METIS_OPTION_ONDISK,
        METIS_OPTION_MINCONN,
        METIS_OPTION_CONTIG,
        METIS_OPTION_COMPRESS,
        METIS_OPTION_CCORDER,
        METIS_OPTION_PFACTOR,
        METIS_OPTION_NSEPS,
        METIS_OPTION_UFACTOR,
        METIS_OPTION_NUMBERING,
        METIS_OPTION_DROPEDGES,
        METIS_OPTION_NO2HOP,
        METIS_OPTION_TWOHOP,
        METIS_OPTION_FAST,
        METIS_OPTION_HELP,
        METIS_OPTION_TPWGTS,
        METIS_OPTION_NCOMMON,
        METIS_OPTION_NOOUTPUT,
        METIS_OPTION_BALANCE,
        METIS_OPTION_GTYPE,
        METIS_OPTION_UBVEC,
    }
}
