#!/usr/bin/env python3
"""Record deployed image tags in images.auto.tfvars, so a plain `terraform plan` shows no change.

    set_image_tag.py <ecr repo> <tag>   one repo deployed on its own (deploy-backend.sh, deploy-collector.sh)
    set_image_tag.py --all <tag>        every repo on one tag (deploy.sh): image_tag, no overrides
"""
import pathlib
import re
import sys

PATH = pathlib.Path(__file__).resolve().parent.parent / "images.auto.tfvars"
HEADER = (
    "# The image tags that run now, so a plain `terraform plan` shows no change. The deploy\n"
    "# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.\n"
)


def main(argv: list[str]) -> None:
    if len(argv) != 2:
        sys.exit(__doc__)
    text = PATH.read_text()
    base = re.search(r'^image_tag\s*=\s*"([^"]*)"', text, re.M).group(1)
    block = re.search(r"^image_tags\s*=\s*\{(.*?)\}", text, re.M | re.S)
    tags = dict(re.findall(r'"([^"]+)"\s*=\s*"([^"]*)"', block.group(1))) if block else {}
    repo, tag = argv
    if repo == "--all":
        base, tags = tag, {}
    else:
        tags[repo] = tag
    lines = "".join(f'  "{r}" = "{t}"\n' for r, t in sorted(tags.items()))
    PATH.write_text(f'{HEADER}image_tag  = "{base}"\nimage_tags = {{\n{lines}}}\n')


if __name__ == "__main__":
    main(sys.argv[1:])
