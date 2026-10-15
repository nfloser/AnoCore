"""Generate v4-only native reels from authoritative C# data; never edit Book UI."""
from pathlib import Path
import xml.etree.ElementTree as ET
from audit_biggest_boy_expanding import profile

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / 'ui/AnoCore'
STYLE = UI / 'styles/custom_game/anocore'
NAMES = ['kebap','beer','cigarette','butter','shot','funnel','g','o','a','t','scatter','book']


def generate():
    lines, pays, strips = profile()
    tree = ET.parse(UI / 'layout/custom_game/anocore/ano_casino_rounds.xml')
    root = tree.getroot()
    ET.SubElement(root.find('styles'), 'include', {'src':'s2r://panorama/styles/custom_game/anocore/ano_casino_biggest.vcss_c'})
    overlay = root.find('.//*[@id="casino_round_root"]')
    overlay.set('class','round-overlay bb-v4')
    window = root.find('.//*[@class="round-window"]')
    original = list(window)
    for child in original:
        classes = child.attrib.get('class','').split()
        if any(c in classes for c in ['biggest-table','biggest-rule-table','book-table','blackjack-table','risk-table','blackjack-actions','risk-actions']): window.remove(child)
    # Keep the source header, counters, stake controls, free-spin action and navigation.
    for label in window.iter('Label'):
        if label.attrib.get('text') == 'AUSZAHLUNG': label.set('text','SPINGEWINN GESAMT')
    table = ET.Element('Panel', {'class':'book-table bb-table','hittest':'false'})
    reels = ET.SubElement(table,'Panel',{'class':'book-reels','hittest':'false'})
    for reel in range(5):
        r = ET.SubElement(reels,'Panel',{'id':f'casino_round_bb_reel{reel}','class':f'book-reel bb-reel bb-reel-{reel}','hittest':'false'})
        offset = ET.SubElement(r,'Panel',{'class':'bb-offset-base','hittest':'false'})
        for bit in range(7):
            offset = ET.SubElement(offset,'Panel',{'id':f'casino_round_bb_offset{reel}_{bit}','class':f'bb-offset bb-bit-{bit}','hittest':'false'})
        motion = ET.SubElement(offset,'Panel',{'class':'bb-motion','hittest':'false'})
        for index in range(303):
            ET.SubElement(motion,'Panel',{'class':f'bb-symbol bb-symbol-{strips[reel][index%100]} bb-row-{index}','hittest':'false'})
        final = ET.SubElement(r,'Panel',{'class':'bb-final','hittest':'false'})
        for row in range(3):
            ET.SubElement(final,'Panel',{'id':f'casino_round_bb_symbol{reel*3+row}','class':'bb-symbol','hittest':'false'})
    for name,cls in [('biggest_win','bb-win'),('biggest_bonus','bonus-summary bb-bonus'),('biggest_spin_win','bb-hidden'),('biggest_total_win','bb-hidden')]:
        ET.SubElement(table,'Label',{'id':'casino_round_'+name,'class':cls,'text':'{s:text}','hittest':'false'})
    rules = ET.Element('Panel',{'class':'bb-rules','hittest':'false'})
    text=['GEWINNPLAN · 5×3 · 10 Linien · nur virtuelle Beträge','Liniengewinn = Einsatz ÷ 10 × Faktor. Drei, vier oder fünf passende Symbole von links. Pro Linie nur der höchste Gewinn; verschiedene Linien addieren sich.','Goldenes Buch ist Wild, nie Scatter. Döner ist ein reguläres Gewinnsymbol. Das Gesicht ist ausschließlich Scatter.']
    labels=['Döner','Bier','Zigarette','Butter','Shot','Trichter','G','O','A','T']
    text += [f'{name}: {p[0]}× / {p[1]}× / {p[2]}×' for name,p in zip(labels,pays)]
    text += ['3/4/5 Gesichter: 10/15/20 Freispiele. Erneuter Trigger: +5, maximal 30 tatsächlich gespielte Freispiele. Ein reguläres Expanding Symbol wird beim Bonusstart gleichverteilt festgelegt und bleibt unverändert.','Trifft das Expanding Symbol eine Walze, füllt es genau drei Positionen. Linien werden einmal auf dem expandierten Feld ausgewertet. Scatter für Verlängerungen zählen vor der Expansion. Einsatz bleibt fest, Gesamtauszahlung am Rundenende.']
    text += [f'Linie {n+1}: '+' – '.join(str(x%3+1) for x in line) for n,line in enumerate(lines)]
    for t in text: ET.SubElement(rules,'Label',{'text':t,'hittest':'false'})
    insertion = list(window).index(window.find('.//*[@id="casino_round_detail"]'))
    window.insert(insertion,table);window.insert(insertion,rules)
    ET.indent(tree,space='  ')
    tree.write(UI / 'layout/custom_game/anocore/ano_casino_biggest.xml',encoding='unicode')
    css='''/* V4 only: the Book stylesheet/layout remain unchanged. */
.bb-v4 .bb-table { visibility:visible; }
.bb-v4 .bb-reel { height:240px; min-height:240px; }
.bb-v4 .book-reels { width:100%; }
.bb-v4 .bb-reel { width:fill-parent-flow(1); }
.bb-v4 .round-detail { font-size:18px; max-height:44px; white-space:normal; }
.bb-symbol { width:100%; height:80px; min-height:80px; background-size:contain; background-position:center; background-repeat:no-repeat; }
.bb-final { width:100%; height:240px; flow-children:down; }
.bb-offset-base, .bb-offset, .bb-motion { width:100%; height:24240px; min-height:24240px; overflow:noclip; }
.bb-offset-base { visibility:collapse; transform:translateY(-16000px); }
.bb-motion { flow-children:none; animation-name:none; animation-timing-function:linear; animation-iteration-count:1; animation-fill-mode:forwards; }
.bb-v4.reels-armed .bb-offset-base, .bb-v4.motion-a .bb-offset-base, .bb-v4.motion-b .bb-offset-base { visibility:visible; }
.bb-v4.reels-armed .bb-final, .bb-v4.motion-a .bb-final, .bb-v4.motion-b .bb-final { visibility:collapse; }
.bb-win { width:100%; horizontal-align:center; text-align:center; font-size:23px; font-weight:bold; color:#90ffaf; }
.bb-bonus { width:100%; text-align:center; white-space:normal; }
.bb-hidden, .bb-rules { visibility:collapse; }
.bb-rules { width:100%; height:330px; flow-children:down; overflow:scroll; padding:12px; border:2px solid #287547; background-color:#09100e; }
.bb-rules Label { width:100%; font-size:17px; white-space:normal; }
.bb-v4.biggest-rules .bb-rules { visibility:visible; }
.bb-v4.biggest-rules .bb-table { visibility:collapse; }
.bb-v4 #casino_round_payout { font-size:28px; font-weight:bold; color:#90ffaf; }
.bb-v4 .round-cover { background-image:url("s2r://panorama/styles/custom_game/anocore/casino/book.vtex"); }
.bb-symbol.winning { border:2px solid #39e38c; animation-name:'bb-win-pulse'; animation-duration:0.8s; animation-iteration-count:infinite; }
.bb-symbol.active-line { border:3px solid #ffde78; }
.bb-symbol.expanded { box-shadow:inset #54ff9a 0px 0px 3px 1px; }
@keyframes 'bb-win-pulse' { 0% { brightness:1; } 50% { brightness:1.6; } 100% { brightness:1; } }
'''
    for i in range(7):css+=f'.bb-bit-{i}.shifted {{ transform:translateY({-80*(1<<i)}px); }}\n'
    for i in range(303):css+=f'.bb-row-{i} {{ position:0px {i*80}px 0px; }}\n'
    for i,name in enumerate(NAMES):css+=f'.bb-symbol-{i} {{ background-image:url("s2r://panorama/styles/custom_game/anocore/casino/biggest_{name}.vtex"); }}\n'
    for reel in range(5):css+=f'.bb-reel-{reel} .bb-motion {{ animation-duration:{5.2+.2*reel:.1f}s; }}\n'
    for n in range(30,130):
        css+=f".bb-v4.motion-a .bb-distance-{n} .bb-motion, .bb-v4.motion-b .bb-distance-{n} .bb-motion {{ animation-name:'bb-travel-{n}'; }}\n"
        frames=['0% { transform:translateY(0px); }']
        for step in range(1,n+1):
            frames += [f'{(step-.2)*100/n:.5f}% {{ transform:translateY({step*80}px); }}',f'{step*100/n:.5f}% {{ transform:translateY({step*80}px); }}']
        css+=f"@keyframes 'bb-travel-{n}' {{ "+' '.join(frames)+' }\n'
    (STYLE / 'ano_casino_biggest.css').write_text(css)
    descriptor=(STYLE / 'casino/biggest_kebap.vtex').read_text().replace('biggest_kebap.png','biggest_book.png')
    (STYLE / 'casino/biggest_book.vtex').write_text(descriptor)


if __name__ == '__main__':generate()
