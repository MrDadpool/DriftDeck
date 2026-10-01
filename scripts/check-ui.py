"""Run with python3 scripts/check-ui.py. Static checks only; WPF still needs Windows QA."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1] / 'src' / 'DriftDeck'
x = '{http://schemas.microsoft.com/winfx/2006/xaml}'
w = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
resources = ET.parse(root / 'App.xaml')
keys = {node.get(x + 'Key') for node in resources.iter()}
for path in root.rglob('*.xaml'):
    tree = ET.parse(path)
    source = path.read_text(encoding='utf-8-sig')
    local = {node.get(x + 'Key') for node in tree.iter()}
    for key in re.findall(r'\{StaticResource ([\w]+)\}', source):
        assert key in keys | local, (path, key)
    code = Path(str(path) + '.cs').read_text()
    for handler in re.findall(r'="(\w+_On\w+)"', source):
        assert re.search(r'\b' + handler + r'\s*\(', code), (path, handler)

main = ET.parse(root / 'MainWindow.xaml').getroot()
code = (root / 'MainWindow.xaml.cs').read_text()
constant = lambda name: float(re.search(rf'{name} = (\d+)', code)[1])
assert float(main.get('Height')) == constant('DockHeight') == 30 + 32 + 40 + 20 + 24
assert float(main.get('MinWidth')) == constant('DockMinWidth')
assert float(main.get('MinHeight')) == constant('CollapsedHeight')
panel = ET.parse(root / 'Controls' / 'PanelHost.xaml').getroot()
title_height = float(panel.find('.//' + w + 'RowDefinition').get('Height'))
shaded = float(re.search(r'ShadedHeight = (\d+)', (root / 'PanelWindow.cs').read_text())[1])
assert shaded == title_height + 2, 'Rolled-up panels must include the title and frame'
print('PASS: XAML, resource references, event handlers, dock and panel collapse geometry')
