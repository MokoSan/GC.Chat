from flask import Flask, request, Response
import json
from langchain.document_loaders import TextLoader, UnstructuredMarkdownLoader
from langchain.indexes import VectorstoreIndexCreator
import urllib.request
import openai

model = "gpt-3.5-turbo"

app = Flask(__name__)
idx = None
bg_idx = None
import os

OPENAI_API_KEY = 'Enter your OPENAI_API_KEY here..'
os.environ["OPENAI_API_KEY"] = OPENAI_API_KEY
openai.api_key = OPENAI_API_KEY

system_message = '''
You are a very helpful chat bot responsible for specifying what state the current state the chat is in for a Garbage Collection Specific application.
You should output 4 main states:

1. 'gc_question': Whether the user has asked a Garbage Collector related question.
2. 'analyze': Whether the user has asked to analyze a specified trace file with extensions: .etl, .etlx or .etl.zip.
3. 'performance_issue': Whether the user has requested assistance with a performance issue of one of the following types: Memory Issues where the size of the Garbage Collector heap has grown, Throughput Issues where the Garbage collector slows down the application or Tail Latency issues where there are spikes in Garbage Collection Pause Time. If the user doesn't specify the performance issue, mark it as a generic_issue.
4. 'unknown_question': If the prompt is a question but doesn't fall into either of the other states.
5. 'unknown': If the prompt doesn't match any of the other cases.

After each message, you should output what the user has requested as correctly formatted JSON with the following keys:
1. States, a list of states.
2. Input, the input input.
3. Response, the response generated by the bot.
4. Files, a list of any trace files are mentioned by the user.

Ensure that the correctly formatted JSON is the only output provided.

If the states contains 'performance_issue', ensure to just mention what type of performance issue it is in Response field of the JSON and also ask the user to take a top level GC trace.
If the states contains 'analyze', list all files that match the following extension in the Files field of the JSON and no other information: .etl, .etlx, .etl.zip.
If the states is 'unknown', the Response field of the JSON should respond with apology message about not being able to get to the details.

Do note that the user message could contain multiple types of state and these all should be mentioned. Convert the output into a valid JSON output.
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
    global bg_idx

    link = "https://raw.githubusercontent.com/Maoni0/mem-doc/master/doc/.NETMemoryPerformanceAnalysis.md"
    f = urllib.request.urlopen(link)
    myfile = f.read()
    with open("./MemDoc.md", 'wb') as wf:
        wf.write(myfile)

    link = 'https://gist.githubusercontent.com/MokoSan/0b520ec2bf1085fd7b0784207f9bce7f/raw/97513a439848a7cbdbc2baabe8dc060475a0f261/bhagwatgita.txt'
    f = urllib.request.urlopen(link)
    myfile = f.read()
    with open('./Bg.txt', 'wb') as wf:
        wf.write(myfile)

    loader = UnstructuredMarkdownLoader('./MemDoc.md')
    loader2 = TextLoader('./Bg.txt')
    idx = VectorstoreIndexCreator().from_loaders([loader])
    bg_idx = VectorstoreIndexCreator().from_loaders([loader2])

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


@app.route('/bg', methods = ['POST'])
def index_2():

    if not initialized:
        initialize()

    rq = json.loads(request.data)
    if 'Query' not in rq:
        return Response("{\"Error\": \"Query must be provided in the payload.\" }", status = 401, mimetype= 'application/json')
    else:
        q = rq['Query']
        result = bg_idx.query(q)
        return json.dumps(result)

if __name__ == '__main__':
    app.run(debug=True)