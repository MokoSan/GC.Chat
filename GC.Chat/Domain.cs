namespace GC.Chat
{
    /*
     * 
      {"files_mentioned":[],"input_prompt":"What is pinning?","performance_issue_type":"","prompt_types":["question"],"response_by_model":" Pinning is a process that indicates to the GC that an object cannot be moved. It is used to prevent fra{files_mentioned:[],input_prompt:What is pinning?,performance_issue_type:,prompt_types:[question],response_by_model: Pinning is a
process that indicates to the GC that an object cannot be moved. It is used to prevent fragmentation in the heap and to help the GC
manage memory more efficiently.}
     */
    public sealed class SourceResponse 
    {
        public string question { get; set; }
        public string sources { get; set; }
        public string answer { get; set; }
    }

    public sealed class ModelResponse
    {
        public List<string> files_mentioned { get; set; }
        public string input_prompt { get; set; }
        public string response_by_model { get; set; }
        public string performance_issue_type { get; set; }
        public List<string> prompt_types { get; set; }
        public string process_name { get; set; }
    }
}
