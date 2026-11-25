namespace OccupancyTracker.Models
{
    public class ProcessStatus<T>
    {
        public bool RequestSuccessful { get; set; }
        public string ResultMessage { get; set; }
        public T? ResultData { get; set; }
        public Exception? ProcessException { get; set; }
        public string? ErrorSqid { get; set; }

        public ProcessStatus<T> Success(T resultData, string resultMessage = "Success")
        {
            RequestSuccessful = true;
            ResultData = resultData;
            ResultMessage = resultMessage;
            return this;
        }

        
        public ProcessStatus<T> Error(Exception processException, string errorSqid, T? resultData, string resultMessage="Error")
        {
            RequestSuccessful = false;
            ResultMessage = resultMessage;
            ResultData = resultData;
            ErrorSqid = errorSqid;
            ProcessException = processException;
            return this;
        }
    }
}
