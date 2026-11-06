# Borland Turbo Vision C++ provenance and distribution notice

TSharpVision is a C#/.NET reimplementation of the Turbo Vision programming model. Its historical reference sources include Borland's C++ Turbo Vision library and TVHC help compiler. This notice records the publisher's public-domain-distribution statement relied on for the original C++ library. TSharpVision's own contributions are licensed under the repository's MIT LICENSE; that MIT grant is not a grant made by Borland.

## Publisher statement: FAQ QNA906, 2 July 1998

Borland/Inprise's Question and Answer Database, QNA906 (also reproduced as article 17285), identifies the public-domain distribution as the C++ library in tv.zip. The following wording is preserved verbatim (trailing whitespace normalized), including "archve" and the original timestamp, from the project's local TV202 source distribution README:

```text
Question and Answer Database

QNA        :Turbo Vision
Category   :C/C++ Language Issues
Platform   :Windows95
Product   :C++Builder  ALL

Question:
Where can I find the public domain version of Turbo Vision?

Answer:
It can be found at
ftp.inprise.com/pub/borlandcpp/devsupport/archive/turbovision/

the turbo vision libraries are in the tv.zip archve, the rest of the
files are examples.

7/2/1998 10:32:32 AM
```

Original publisher locations: [QNA906](http://www.inprise.com/devsupport/bcppbuilder/faq/QNA906.html) and [article 17285](http://community.borland.com/article/0,1410,17285,00.html). Historical distribution: `ftp://ftp.inprise.com/pub/borlandcpp/devsupport/archive/turbovision/tv.zip`.

Corroborating reproductions: [jserv/tvision README](https://github.com/jserv/tvision#readme), [Debian mailing-list reproduction, January 2000](https://lists.debian.org/debian-legal/2000/01/msg00054.html), and [article 17285 mirror](https://rxlib.ru/faqs/faqc_en/17285.html). These are reproductions, not newly issued permissions. The original publisher URLs and an attempted web-archive copy were not retrievable during this review; a directly fetched publisher-hosted original has not been authenticated here. The local original archive notices were authenticated against the local tv.zip bytes, not against a newly downloaded publisher copy.

## Original notices and scope

The local tv.zip contains disclaim.txt, readme.txt, Include.zip and Source.zip. Unmodified copies of its [readme.txt](BORLAND-README.txt) and [disclaim.txt](BORLAND-DISCLAIMER.txt) accompany this notice. They retain the publisher's warranty disclaimer, limitation of liability and contractor attribution. The disclaimer is not a substitute for the FAQ's distribution statement and is not relabelled as a modern licence grant.

For example, the original Source.zip TVIEW.CPP and TSCREEN.CPP headers state:

```text
Copyright (c) 1994 by Borland International
All Rights Reserved.
```

These historical attributions are retained, not rewritten as TSharpVision copyright. The public-domain statement relied on here concerns the original **Borland C++ Turbo Vision library distributed as tv.zip**. It does not extend this conclusion to Turbo Pascal editions, other commercial Borland products or original manuals absent from that archive. Later GPL/BSD/MIT ports have their own authors' contributions and terms; none of their licences is substituted for the publisher statement or presented as Borland's licence. Consulting a later port as a behavioural reference is not a claim to relicense its additions.

## Archive integrity and redistribution

The following SHA-256 values identify the local artifacts examined:

- tv.zip: `dee213fc69988bd4fecb73a7e8474ba832ad9cf4d03ad09eb1f398570e01a610`
- BORLAND-README.txt (tv.zip/readme.txt): `9997394ee62e2ee981397a05cf3a095a48436bb415d9d1befedcb6bd8f8c95e2`
- BORLAND-DISCLAIMER.txt (tv.zip/disclaim.txt): `6f6ebbd26f6a4c4f118ea3c0f6cb37907037a47e80b1a3e41ab504ad8eb9f203`

All twelve TSharpVision NuGet packages carry this notice and the two original archive texts at their package roots, including tools that bundle core and libraries that depend on it. Their MIT metadata describes TSharpVision's own licence. Unicode UCD 16.0 / CLDR 46 attribution and licence text remain separately in THIRD-PARTY-NOTICES.md (canonical repository source: resources/licences/THIRD-PARTY-NOTICES.md).
