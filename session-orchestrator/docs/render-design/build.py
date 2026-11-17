import json, os, sys
root, src, inbox, out = sys.argv[1:5]
data = {}
for k in os.listdir(os.path.join(root, 'templates')):
    d = os.path.join(root, 'templates', k)
    j = lambda n: json.load(open(os.path.join(d, n), encoding='utf-8'))
    item = {'view': j('view.json'), 'example': j('example.json'), 'purpose': j('meta.json')['purpose']}
    if k == 'notice':
        name = item['example']['kind']
        item['renders'] = {name: open(os.path.join(d, 'render', name + '.md'), encoding='utf-8').read().replace('\r\n', '\n')}
    data[k] = item
lines = open(inbox, encoding='utf-8').read().split('\n')
start = next(i for i, l in enumerate(lines) if l.startswith(':root {'))
end = next(i for i, l in enumerate(lines) if i > start and l.startswith(':root[data-theme="dark"]'))
end = next(i for i in range(end, len(lines)) if lines[i].strip().endswith('color-scheme: dark; }'))
tokens = '\n'.join(lines[start:end + 1])
page = open(src, encoding='utf-8').read().replace('/*TOKENS*/', tokens).replace('/*DATA*/', json.dumps(data, ensure_ascii=False).replace('</', '<\/'))
open(out, 'w', encoding='utf-8').write(page)
print(len(page))
