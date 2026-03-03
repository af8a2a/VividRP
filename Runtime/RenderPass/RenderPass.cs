namespace VividRP.Runtime
{
    public interface IRenderPass
    {
        //Prepare runtime resource(e.g:dynamic count buffer)
        void Prepare();

        void Record();
    }


    public interface IComputePass : IRenderPass
    {
    }


    public interface IRasterPass : IRenderPass
    {
    }


    public interface IUnsafePass : IRenderPass
    {
    }
}