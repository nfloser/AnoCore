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
    # Dedicated v4 composition approved in the design preview; shared Book source stays immutable.
    window.clear(); window.set('class', 'round-window'); window.set('hittest','true')
    def panel(parent, cls, id=None):
        attrs={'class':cls}
        if id:attrs['id']='casino_round_'+id
        return ET.SubElement(parent,'Panel',attrs)
    def label(parent, text, id=None, cls=''):
        attrs={'text':text,'hittest':'false'}
        if id:attrs['id']='casino_round_'+id
        if cls:attrs['class']=cls
        return ET.SubElement(parent,'Label',attrs)
    def button(parent, id, text, cls='button'):
        b=ET.SubElement(parent,'Button',{'id':'casino_round_'+id,'class':cls})
        label(b,text);return b
    header=panel(window,'bb-header')
    button(header,'back','‹ SPIELAUSWAHL')
    label(header,'ANOMEME · ANOCASINO',cls='bb-brand')
    label(header,'{s:text}','title','bb-title')
    button(header,'close','✕')
    accounts=panel(window,'bb-accounts')
    for name,title in [('account','PROFILKONTO'),('bankroll','AUTOMATENGUTHABEN')]:
        c=panel(accounts,'bb-account');label(c,title);label(c,'{s:text}',name)
    table = ET.Element('Panel', {'class':'book-table bb-table','hittest':'false'})
    reels = ET.SubElement(table,'Panel',{'class':'book-reels','hittest':'false'})
    for reel in range(5):
        if reel: panel(reels,'bb-divider')
        r = ET.SubElement(reels,'Panel',{'id':f'casino_round_bb_reel{reel}','class':f'book-reel bb-reel bb-reel-{reel}','hittest':'false'})
        # Four bounded panels, updated from the frozen physical strip during the animation.
        motion = ET.SubElement(r,'Panel',{'class':'bb-motion','hittest':'false'})
        for row in range(4):
            ET.SubElement(motion,'Panel',{'id':f'casino_round_bb_motion{reel}_{row}','class':'bb-symbol','hittest':'false'})
        final = ET.SubElement(r,'Panel',{'class':'bb-final','hittest':'false'})
        for row in range(3):
            ET.SubElement(final,'Panel',{'id':f'casino_round_bb_symbol{reel*3+row}','class':'bb-symbol','hittest':'false'})
    bonus=panel(table,'bb-bonus-strip')
    for name,title in [('bb_remaining','FREISPIELE'),('bb_expanding','EXPANDING SYMBOL'),('bb_free_win','FREISPIELGEWINN'),('bb_series','SERIENGEWINN')]:
        c=panel(bonus,'bb-bonus-item');label(c,title);label(c,'{s:text}',name)
    for name in ['biggest_win','biggest_bonus','biggest_spin_win','biggest_total_win']:
        label(table,'{s:text}',name,'bb-hidden')
    rules = ET.Element('Panel',{'class':'bb-rules','hittest':'false'})
    text=['GEWINNPLAN · 5×3 · 10 Linien · nur virtuelle Beträge','Liniengewinn = Einsatz ÷ 10 × Faktor. Drei, vier oder fünf passende Symbole von links. Pro Linie nur der höchste Gewinn; verschiedene Linien addieren sich.','Biggest-Boy-Schärpe ist Wild, nie Scatter. Döner ist ein reguläres Gewinnsymbol. Süßilix ist ausschließlich Freispiel-Scatter.']
    labels=['Döner','Bier','Zigarette','Butter','Shot','Trichter','G','O','A','T']
    text += [f'{name}: {p[0]}× / {p[1]}× / {p[2]}×' for name,p in zip(labels,pays)]
    text += ['3/4/5 Süßilix-Symbole: 10/15/20 Freispiele. Erneuter Trigger: +5, maximal 30 tatsächlich gespielte Freispiele. Ein reguläres Expanding Symbol wird beim Bonusstart gleichverteilt festgelegt und bleibt unverändert.','Trifft das Expanding Symbol eine Walze, füllt es genau drei Positionen. Linien werden einmal auf dem expandierten Feld ausgewertet. Scatter für Verlängerungen zählen vor der Expansion. Einsatz bleibt fest, Gesamtauszahlung am Rundenende.']
    text += [f'Linie {n+1}: '+' – '.join(str(x%3+1) for x in line) for n,line in enumerate(lines)]
    for t in text: ET.SubElement(rules,'Label',{'text':t,'hittest':'false'})
    window.append(table);window.append(rules)
    label(window,'{s:text}','detail','round-detail')
    controls=panel(window,'bb-controls')
    stake=panel(controls,'bb-stake')
    label(stake,'EINSATZ');stakebuttons=panel(stake,'bb-stake-buttons stake-controls')
    button(stakebuttons,'minus','−');label(stakebuttons,'{s:text}','stake');button(stakebuttons,'plus','+')
    payout=panel(controls,'bb-payout');label(payout,'RUNDENGEWINN');label(payout,'{s:text}','payout')
    bank=panel(controls,'bb-bottom-bank');label(bank,'AUTOMATENGUTHABEN');label(bank,'{s:text}','bankroll_footer')
    actions=panel(controls,'bb-actions')
    button(actions,'play','↻ DREHEN','button primary bb-spin')
    free=panel(actions,'book-actions');button(free,'free','↻ FREISPIEL','button primary bb-spin')
    label(window,'{s:text}','status','status')
    nav=panel(window,'bb-navigation')
    button(nav,'rules','REGELN / GEWINNTABELLE');button(nav,'refresh','AKTUALISIEREN')
    button(nav,'retry','BUCHUNG PRÜFEN','button retry')
    label(window,'Virtuelles Guthaben · keine Echtgeld-Auszahlung',cls='note')
    ET.indent(tree,space='  ')
    tree.write(UI / 'layout/custom_game/anocore/ano_casino_biggest.xml',encoding='unicode')
    css = '''/* V4 only: the Book stylesheet/layout remain unchanged. */
.bb-v4 .bb-table { visibility:visible; }
.bb-v4 .bb-reel { height:480px; min-height:480px; }
.bb-v4 .book-reels { width:100%; }
.bb-v4 .bb-reel { width:fill-parent-flow(1); }
.bb-v4 .round-detail { font-size:18px; max-height:44px; white-space:normal; }
.bb-symbol { width:100%; height:160px; min-height:160px; background-size:contain; background-position:center; background-repeat:no-repeat; }
.bb-final { width:100%; height:480px; flow-children:down; }
.bb-v4 .bb-reel { overflow:clip; }
.bb-motion { width:100%; height:640px; min-height:640px; flow-children:down; visibility:collapse; }
.bb-v4.reels-armed .bb-motion, .bb-v4.motion-a .bb-motion, .bb-v4.motion-b .bb-motion { visibility:visible; }
.bb-reel.bb-step-a .bb-motion { animation-name:'bb-step-a'; animation-duration:0.04s; animation-timing-function:ease-out; }
.bb-reel.bb-step-b .bb-motion { animation-name:'bb-step-b'; animation-duration:0.04s; animation-timing-function:ease-out; }
@keyframes 'bb-step-a' { 0% { transform:translateY(-160px); } 100% { transform:translateY(0px); } }
@keyframes 'bb-step-b' { 0% { transform:translateY(-160px); } 100% { transform:translateY(0px); } }
.bb-v4.reels-armed .bb-reel.stopped .bb-motion, .bb-v4.motion-a .bb-reel.stopped .bb-motion, .bb-v4.motion-b .bb-reel.stopped .bb-motion { visibility:collapse; }
.bb-v4.reels-armed .bb-reel.stopped .bb-final, .bb-v4.motion-a .bb-reel.stopped .bb-final, .bb-v4.motion-b .bb-reel.stopped .bb-final { visibility:visible; }
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
.bb-symbol.winning { border:3px solid #ff3030; animation-name:'bb-win-pulse'; animation-duration:0.8s; animation-iteration-count:infinite; }
.bb-symbol.active-line { border:4px solid #ff3030; }
.bb-symbol.expanded { box-shadow:inset #54ff9a 0px 0px 3px 1px; }
@keyframes 'bb-win-pulse' { 0% { brightness:1; } 50% { brightness:1.6; } 100% { brightness:1; } }

.bb-v4 .round-window { width:100%; height:100%; padding:20px 28px; border:1px solid #235937; border-radius:0px; background-color:#060d09; }
.bb-header { width:100%; height:58px; flow-children:right; border-bottom:1px solid #235937; }
.bb-brand { color:#91ed72; font-size:20px; width:25%; vertical-align:center; text-align:center; }
.bb-title { width:fill-parent-flow(1); font-size:34px; font-weight:bold; color:#b1ef71; text-align:center; vertical-align:center; }
.bb-accounts { width:100%; height:76px; flow-children:right; horizontal-align:center; margin:10px 0px; }
.bb-account { width:49%; height:64px; flow-children:down; horizontal-align:center; background-color:#101e15; border:2px solid #366942; border-radius:7px; margin:0px 6px; padding:5px; }
.bb-account Label { horizontal-align:center; vertical-align:center; text-align:center; font-size:18px; }
.bb-account #casino_round_account, .bb-account #casino_round_bankroll { font-weight:bold; }
.bb-v4 .bb-table { padding:0px; border:1px solid #235937; background-color:#07190e; }
.bb-v4 .book-reels { margin:0px; height:480px; }
.bb-v4 .bb-reel { margin:0px; border:0px solid transparent; background-color:#07190e; }
.bb-divider { width:2px; height:480px; background-color:#356344; border-right:1px solid #6a9b58; }
.bb-bonus-strip { width:100%; height:70px; flow-children:right; border-top:1px solid #356344; background-color:#08120c; }
.bb-bonus-item { width:25%; height:100%; flow-children:down; }
.bb-bonus-item Label { horizontal-align:center; font-size:17px; }
.bb-bonus-item #casino_round_bb_remaining, .bb-bonus-item #casino_round_bb_expanding, .bb-bonus-item #casino_round_bb_free_win, .bb-bonus-item #casino_round_bb_series { color:#a6f47a; font-size:23px; font-weight:bold; }
.bb-controls { width:100%; height:112px; flow-children:right; margin-top:10px; }
.bb-stake, .bb-payout, .bb-bottom-bank { width:24%; height:100%; flow-children:down; background-color:#101e15; border:2px solid #366942; border-radius:7px; padding:9px; margin-right:1%; }
.bb-actions { width:24%; height:100%; flow-children:down; }
.bb-stake > Label, .bb-payout > Label, .bb-bottom-bank > Label { horizontal-align:center; text-align:center; font-size:17px; }
.bb-bottom-bank #casino_round_bankroll_footer { horizontal-align:center; text-align:center; font-size:30px; font-weight:bold; color:#a6f47a; }
.bb-stake-buttons { horizontal-align:center; flow-children:right; }
.bb-stake-buttons Label { vertical-align:center; font-size:25px; }
.bb-v4 #casino_round_payout { horizontal-align:center; text-align:center; font-size:38px; font-weight:bold; color:#a6f47a; }
.bb-v4 .bb-actions .book-actions { width:100%; }
.bb-v4 .bb-spin { width:100%; height:92px; margin:0px; background-color:#469d29; border:1px solid #b0ee86; border-radius:12px; }
.bb-spin Label { horizontal-align:center; vertical-align:center; font-size:30px; font-weight:bold; color:#061107; }
.bb-v4 .status { font-size:16px; max-height:26px; margin:0px; }
.bb-v4 .round-detail { font-size:17px; height:38px; margin:0px; }
.bb-navigation { flow-children:right; horizontal-align:center; margin-top:8px; }
.bb-v4 .note { horizontal-align:center; font-size:13px; }
.bb-v4 .bb-rules { height:550px; }
.bb-v4 .status { color:#ffaaaa; text-align:center; horizontal-align:center; }
.bb-v4 .bb-stake-buttons { width:100%; horizontal-align:center; }
.bb-v4 #casino_round_stake { horizontal-align:center; text-align:center; }

'''
    for i,name in enumerate(NAMES):
        css += f'.bb-symbol-{i} {{ background-image:url("s2r://panorama/styles/custom_game/anocore/casino/biggest_{name}.vtex"); }}\n'
    (STYLE / 'ano_casino_biggest.css').write_text(css)
    descriptor=(STYLE / 'casino/biggest_kebap.vtex').read_text().replace('biggest_kebap.png','biggest_book.png')
    (STYLE / 'casino/biggest_book.vtex').write_text(descriptor)


if __name__ == '__main__':generate()
