from flask import Flask, request, Response
import json
from langchain.document_loaders import TextLoader, UnstructuredMarkdownLoader
from langchain.indexes import VectorstoreIndexCreator
import urllib.request
import openai

model = "gpt-3.5-turbo"

app = Flask(__name__)
idx = None
import os

OPENAI_API_KEY = 'INSERT OPEN AI KEY'
os.environ["OPENAI_API_KEY"] = OPENAI_API_KEY
openai.api_key = OPENAI_API_KEY

system_message = '''
Your task is to determine to respond to prompts as a very helpful Garbage Collection Chat Bot by performing the following actions and only output a correctly formatted JSON object, the rest of the details shouldn't be outputted:

Step 1: Categorizing the user prompt based on one or more of the following choices:

1. 'question': Whether the user has asked a question - this could be question related to garbage collection or could be a general question.
2. 'analyze': Whether the user has asked to analyze a specified trace file with extensions: .etl, .etlx or .etl.zip.
3. 'performance_issue': Whether the user has requested assistance with a performance issue of one of the following types: 'memory_issue' where the size of the Garbage Collector heap has grown, 'throughput_issue' where the Garbage collector slows down the application or 'tail_latency_issue' where there are spikes in Garbage Collection Pause Time.
4. 'unknown': If the prompt doesn't match any of the other cases.

Step 2: Extracting the appropriate details based on the categories determined in Step 1:

1. If one of the categories is 'analyze' extract the .etl, .etlx or .etl.zip file. If the user has provided a process name of analyze, extract that information, as well.
2. If one of the categories is 'performance_issue' extract the performance issue.

Step 3: Outputting a JSON object with the following keys:
prompt_types, input_prompt, response_by_model, files_mentioned, performance_issue_type.

The prompt_types field should be a list of extracted states e.g., ['question', 'analyze'].
The input_prompt field should be the user prompt.
The performance_issue_type should be the extracted performance issue if given and an empty string if not.
The response_by_model should contain any additional necessary information to be conveyed to the user.
The files_mentioned should be a list of any trace files are mentioned by the user. If the states doesn't contain 'analyze', this should be an empty array.
The process_name should be the name of the process mentioned by the user. If the state doesn't contain 'analyze', this should be an empty string.
'''

def get_completion(prompt):
    messages = [
        {"role": "system", "content" : system_message },
        {"role": "user", "content": prompt}
        ]
    response = openai.ChatCompletion.create(
        model=model,
        messages=messages,
        temperature=0, # this is the degree of randomness of the model's output
    )
    return json.loads(response.choices[0].message["content"])

def get_completion_model(prompt):
    messages = [{"role": "user", "content": prompt} ]
    response = openai.ChatCompletion.create(
        model=model,
        messages=messages,
        temperature=0, # this is the degree of randomness of the model's output
    )
    return response

initialized = False
def initialize():
    global idx

    link = "https://raw.githubusercontent.com/Maoni0/mem-doc/master/doc/.NETMemoryPerformanceAnalysis.md"
    f = urllib.request.urlopen(link)
    myfile = f.read()
    with open("./MemDoc.md", 'wb') as wf:
        wf.write(myfile)

    loader = UnstructuredMarkdownLoader('./MemDoc.md')
    idx = VectorstoreIndexCreator().from_loaders([loader])

@app.route('/modelquery', methods = ['POST'])
def model_query():
    rq = json.loads(request.data)
    if 'Query' not in rq:
        return Response("{\"Error\": \"Query must be provided in the payload.\" }", status = 401, mimetype= 'application/json')
    else:
        q = rq['Query']
        result = get_completion_model(q)
        return result.choices[0].message["content"]

@app.route('/gc_modelquery', methods = ['POST'])
def gc_model_query():
    rq = json.loads(request.data)
    if 'Query' not in rq:
        return Response("{\"Error\": \"Query must be provided in the payload.\" }", status = 401, mimetype= 'application/json')
    else:
        q = rq['Query']
        result = get_completion(q)
        if 'question' in result['prompt_types'] or 'unknown' in result['prompt_types']:
            if not initialized:
                initialize()
            qry = idx.query_with_sources(q)
            result['response_by_model'] = qry['answer'] + " Source: " + qry['sources']
        return result

@app.route('/query', methods = ['POST'])
def index():

    if not initialized:
        initialize()

    rq = json.loads(request.data)
    if 'Query' not in rq:
        return Response("{\"Error\": \"Query must be provided in the payload.\" }", status = 401, mimetype= 'application/json')
    else:
        q = rq['Query']
        result = idx.query(q)
        return json.dumps(result)

@app.route('/querywithsource', methods = ['POST'])
def index_3():

    if not initialized:
        initialize()

    rq = json.loads(request.data)
    if 'Query' not in rq:
        return Response("{\"Error\": \"Query must be provided in the payload.\" }", status = 401, mimetype= 'application/json')
    else:
        q = rq['Query']
        result = idx.query_with_sources(q)
        return json.dumps(result)

if __name__ == '__main__':
    app.run(debug=True)