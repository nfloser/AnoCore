"""Original faces, alternating native opacity holds, build delivery and stable Book."""
from pathlib import Path
import hashlib,json,re,unittest
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[2]
UI=ROOT/'ui/AnoCore'

def locked_source(path):
    data = (ROOT / path).read_bytes()
    if path.endswith('ano_casino_rounds.css'):
        data = data.split(b'\n/* Blackjack table:', 1)[0]
    return data


class CoinflipFacesTests(unittest.TestCase):
    def test_originals_and_both_build_paths(self):
        for name,item in json.loads((ROOT/'docs/coinflip-original-assets.json').read_text()).items():
            path=UI/'styles/custom_game/anocore/casino'/name
            self.assertEqual(item['sha256'],hashlib.sha256(path.read_bytes()).hexdigest())
            self.assertIn(name,path.with_suffix('.vtex').read_text())
            for build in ['build.ps1','build-casino.ps1']:
                self.assertIn('"'+path.stem+'"',(UI/build).read_text())
    def test_two_face_panels_and_complementary_visible_holds(self):
        tree=ET.parse(UI/'layout/custom_game/anocore/ano_casino_coinflip.xml')
        coin=tree.find('.//*[@id="casino_coinflip_coin"]')
        self.assertEqual(['coin-face coin-heads','coin-face coin-tails'],[p.attrib['class'] for p in coin])
        css=(UI/'styles/custom_game/anocore/ano_casino_coinflip.css').read_text()
        frames=[]
        for name in ['coin-heads-visible','coin-tails-visible']:
            body=css.split("@keyframes '"+name+"'",1)[1].split('@keyframes',1)[0]
            frames.append([(int(t),int(v)) for t,v in re.findall(r'(\d+)%\s*\{\s*opacity:(\d)',body)])
        self.assertEqual([0,24,25,74,75,100],[t for t,v in frames[0]])
        for a,b in zip(*frames):self.assertEqual(a[0],b[0]);self.assertEqual(1,a[1]+b[1])
        self.assertIn('animation-duration:0.48s',css)
        self.assertIn('.land-tails .coin-tails { opacity:1; }',css)
        for path,expected in json.loads((ROOT/'docs/biggest-boy-v4-source-lock.json').read_text()).items():
            self.assertEqual(expected,hashlib.sha256(locked_source(path)).hexdigest())

if __name__=='__main__':unittest.main()
