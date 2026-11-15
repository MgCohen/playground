# Pattern languages and pattern catalogs in architecture and software

Research date: 2026-11-14. About 23 tool calls. Each item in "Cited Findings" has a source. Items that I know only from background knowledge are in "Gaps", with the label "unverified".

## Christopher Alexander: A Pattern Language (1977), The Timeless Way of Building, The Nature of Order

### Takeaway
An Alexandrian pattern has a fixed sequence: picture, context paragraph with links to larger patterns, bold problem headline, body, "Therefore:", bold solution as an instruction, diagram, and a paragraph with links to smaller patterns. The 253 patterns go from large (towns) to small (construction). The user selects a subset for each project. Later, *The Nature of Order* replaces the fixed catalog with a step-by-step process of structure-preserving transformations.

### Cited Findings
- The book has 253 patterns. The authors write: "All 253 patterns together form a language." — [Wikipedia: A Pattern Language](https://en.wikipedia.org/wiki/A_Pattern_Language)
- The book has three parts: Towns, Buildings, Construction. The order goes from regions, cities and neighborhoods down to "the level of doorknobs." — [Wikipedia: A Pattern Language](https://en.wikipedia.org/wiki/A_Pattern_Language)
- The asterisks on each pattern show "our degree of faith in these hypotheses". The authors treat each pattern as a tentative hypothesis that can change with new experience. — [Wikipedia: A Pattern Language](https://en.wikipedia.org/wiki/A_Pattern_Language)
- A pattern refers to another pattern by its number, for example **(12)** for Community of 7,000. The book is a network of patterns. — [Wikipedia: A Pattern Language](https://en.wikipedia.org/wiki/A_Pattern_Language)
- The solutions are abstract, so that readers can adapt them to their preferences and local conditions. The patterns are for ordinary people who improve a neighborhood, design a home, or design an office, workshop or school with colleagues. — [Wikipedia: A Pattern Language](https://en.wikipedia.org/wiki/A_Pattern_Language)
- *The Timeless Way of Building* is volume 1 of the Center for Environmental Structure Series. *A Pattern Language* is volume 2. — [Wikipedia: A Pattern Language](https://en.wikipedia.org/wiki/A_Pattern_Language)
- The Alexandrian template, from a summary of the book's "Using this book" section (Kerievsky, 1999):
  1. A picture that shows "an archetypal example of the pattern."
  2. An introductory paragraph that sets "the context for the pattern, by explaining how it helps to complete certain larger patterns."
  3. A bold headline: "This headline gives the essence of the problem in one or two sentences."
  4. The body of the problem, "the longest section": empirical background, evidence, and the range of forms of the pattern.
  5. The word "Therefore:", which ends the problem section.
  6. A bold solution headline, the "heart of the pattern", stated as an instruction.
  7. A diagram of the solution, with labels on its main parts.
  8. A closing paragraph that ties the pattern to the smaller patterns that complete or embellish it.
  — [Industrial Logic, "The Patterns Template" (Kerievsky)](https://www.industriallogic.com/papers/template.html)
- In the book, three diamonds mark the start of the problem, and the bold headline follows them. A second set of three diamonds closes the main body after the diagram. Secondary sources do not fully agree on the order. — [Carleton slides](https://people.scs.carleton.ca/~deugo/Patterns/Agent/Presentations/AgentPatterns/tsld090.htm); [Carleton slides, next](https://people.scs.carleton.ca/~deugo/Patterns/Agent/Presentations/AgentPatterns/tsld091.htm)
- Pictures and sketches in the pattern can also show "what _not_ to do". The body can contain stories and expert quotes. — [Industrial Logic](https://www.industriallogic.com/papers/template.html)
- *The Nature of Order* (2002–2004) changes the fifteen geometric properties into structure-preserving transformations: operations that act on a structure, not only the results of other operations. — [Wikipedia: The Nature of Order](https://en.wikipedia.org/wiki/The_Nature_of_Order); [Stefan Lesser notes: Fifteen transformations](https://nature-of-order.stefan-lesser.com/b2-c2-fifteen-transformations)
- In the process, each step uses one or more of the fifteen transformations to differentiate and strengthen the existing whole. Living structure comes from repeated "unfolding". Alexander contrasts this with "structure-destroying transformations". — [Stefan Lesser notes](https://nature-of-order.stefan-lesser.com/b2-c2-how-the-fifteen-properties-appear-in-nature-from-the-unfolding-of-wholeness); [aardrock wiki: The Process of Creating Life](https://wiki.aardrock.com/The_Process_of_Creating_Life)
- Alexander does not give a rigorous proof that the fifteen transformations are complete. — [Stefan Lesser notes](https://nature-of-order.stefan-lesser.com/b2-c2-fifteen-transformations)

### Inferences
- Linking is in the template itself. The first paragraph links up (to larger patterns). The last paragraph links down (to smaller patterns). Thus each pattern is a node in a directed graph, and nesting comes from these links, not from a separate index.
- Fixed versus judgment: the problem and the solution are fixed and in bold. The solution is abstract on purpose, so the dimensions, materials and form are the judgment of the user. The star rating tells the user how much freedom to take.
- When no pattern fits, the Alexandrian answer is to write a new pattern. The patterns are hypotheses, and the user can change them. *The Nature of Order* goes further: it replaces the selection of patterns with a process of transformations on the existing whole.

### Gaps
- Unverified (background knowledge): the star scale has three levels. Two asterisks mean a true invariant, one asterisk means some progress toward an invariant, and no asterisk means that the authors are sure they did not find the invariant. I did not get a primary source for this scale.
- Unverified: in "Using this book", Alexander tells the user to pick the pattern that best describes the overall scope of the project, then tick the smaller patterns that it links to, and add the patterns that are not in the list. The result is a project-specific language. I did not reach the primary text.
- Unverified: *The Timeless Way of Building* describes pattern languages as "generative" and describes the "quality without a name". *The Nature of Order* volume 2 describes "generative sequences" and the 15 properties (Levels of Scale, Strong Centers, Boundaries, and others). I did not fetch a source that lists them.

## Gang of Four: Design Patterns (1994)

### Takeaway
GoF uses a fixed template of 13 headings and classifies 23 patterns by purpose (creational, structural, behavioral) and scope (class, object). The book gives six ways to select a pattern, for example "examine a cause of redesign" and "consider what should be variable".

### Cited Findings
- Template headings with their descriptions:
  - **Name**: "A descriptive and unique name that helps in identifying and referring to the pattern."
  - **Intent**: "A description of the goal behind the pattern and the reason for using it."
  - **Also Known As**: "Other names for the pattern."
  - **Motivation**: "A scenario consisting of a problem and a context in which this pattern can be used."
  - **Applicability**: "Situations in which this pattern is usable; the context for the pattern."
  - **Structure**: "A graphical representation of the pattern." Class and interaction diagrams.
  - **Participants**: "A listing of the classes and objects used in the pattern and their roles in the design."
  - **Collaboration**: "A description of how classes and objects used in the pattern interact with each other."
  - **Consequences**: "A description of the results, side effects, and trade offs caused by using the pattern."
  - **Implementation**: "A description of an implementation of the pattern; the solution part of the pattern."
  - **Sample Code**: "An illustration of how the pattern can be used in a programming language."
  - **Known Uses**: "Examples of real usages of the pattern."
  - **Related Patterns**: other patterns with a relationship, and how this pattern is different from similar ones.
  — [Wikipedia: Software design pattern](https://en.wikipedia.org/wiki/Software_design_pattern)
- Wikipedia uses "Name" and "Collaboration". The book uses "Pattern Name and Classification" and "Collaborations" (see Gaps). — [Wikipedia: Software design pattern](https://en.wikipedia.org/wiki/Software_design_pattern)
- The 23 patterns in three categories. Creational: Abstract Factory, Builder, Factory Method, Prototype, Singleton. Structural: Adapter, Bridge, Composite, Decorator, Facade, Flyweight, Proxy. Behavioral: Chain of Responsibility, Command, Interpreter, Iterator, Mediator, Memento, Observer, State, Strategy, Template Method, Visitor. — [Wikipedia: Design Patterns](https://en.wikipedia.org/wiki/Design_Patterns)
- Two principles: "Program to an interface, not an implementation." and "Favor 'object composition' over 'class inheritance'." — [Wikipedia: Design Patterns](https://en.wikipedia.org/wiki/Design_Patterns)
- "How to Select a Design Pattern" gives six approaches:
  1. Consider how design patterns solve design problems (Section 1.6: find objects, object granularity, interfaces).
  2. Scan the Intent sections (Section 1.4). Use Table 1.1 to narrow the search.
  3. Study how patterns interrelate, with the relationship diagram (Figure 1.1).
  4. Study patterns of like purpose. Each chapter ends with a comparison section.
  5. Examine a cause of redesign, then look at the patterns that help prevent it.
  6. Consider what should be variable in your design. Table 1.2 maps each pattern to the aspect that it lets you vary.
  — [GoF chapter 1 copy (istu.ru)](https://box.cs.istu.ru/public/docs/devel/patterns/Design%20Patterns/lowres/sample.htm)
- A course deck says to apply a pattern only when the flexibility it gives is necessary. — [edeleastar course slides](https://edeleastar-portfolio.github.io/design-patterns/topic05-gof-patterns/talk-5-selection/selection.pdf)
- Some patterns are conceptually near to each other, so novice developers select the wrong pattern. — [Polyzoidou et al. 2023, SPE](https://sites.uom.gr/a.ampatzoglou/public_html/papers/polyzoidou2023spe.pdf)
- Criticism: Peter Norvig showed that direct language support in Lisp or Dylan makes 16 of the 23 patterns simpler or unnecessary. "Inappropriate use of patterns may unnecessarily increase complexity." — [Wikipedia: Software design pattern](https://en.wikipedia.org/wiki/Software_design_pattern)

### Inferences
- In GoF, selection is a lookup from the problem side (Intent, causes of redesign, what varies). Alexander selects by the scale and links of the project.
- Fixed versus judgment: Intent, Structure, Participants and Collaborations are fixed. Implementation and Sample Code show one possible form. Consequences lists the trade-offs that the designer must weigh.
- "Consider what should be variable" is the GoF answer to a problem that matches no pattern: find the part that varies and encapsulate it with the two principles above.

### Gaps
- Unverified (background knowledge): the book's eight "Causes of redesign", each with the patterns that prevent it: (1) creating an object by specifying a class explicitly (Abstract Factory, Factory Method, Prototype); (2) dependence on specific operations (Chain of Responsibility, Command); (3) dependence on hardware and software platform (Abstract Factory, Bridge); (4) dependence on object representations or implementations (Abstract Factory, Bridge, Memento, Proxy); (5) algorithmic dependencies (Builder, Iterator, Strategy, Template Method, Visitor); (6) tight coupling (Abstract Factory, Bridge, Chain of Responsibility, Command, Facade, Mediator, Observer); (7) extending functionality by subclassing (Bridge, Chain of Responsibility, Composite, Decorator, Observer, Strategy); (8) inability to alter classes conveniently (Adapter, Decorator, Visitor). A search did not find a primary source for this list.
- Unverified: the book heading is "Pattern Name and Classification", and the book uses "Collaborations" (plural). The four essential elements are pattern name, problem, solution, consequences. "How to Use a Design Pattern" has seven steps.
- I did not get the content of Figure 1.1 (pattern relationships). It is a graph with labeled edges, for example "Composite: adding responsibilities to objects: Decorator".

## Pattern community formats: Portland, Coplien, Alexandrian, PLoP pattern writing

### Takeaway
The PLoP community does not require one template. Meszaros and Doble name a minimum set of "Mandatory Elements Present" and a set of "Optional Elements When Helpful", and they make "Visible Forces" a pattern of its own. The names and the order of the elements can change from one form to another.

### Cited Findings
- Meszaros and Doble, "A Pattern Language for Pattern Writing", *Pattern Languages of Program Design 3* (1998), pp. 529–574. The paper is its own running example. — [hillside.net](https://hillside.net/language-for-pattern-writing)
- Pattern list of the paper:
  - A. Context-Setting Patterns: Pattern; Pattern Language.
  - B. Pattern Structure Patterns: Mandatory Elements Present; Optional Elements When Helpful; Visible Forces; Single-Pass Readable; Skippable Sections; Findable Sections.
  - C. Pattern Naming and Referencing Patterns: Relationship to Other Patterns; Readable References to Patterns; (External) Pattern Thumbnail; Evocative Pattern Name; Noun Phrase Name; Meaningful Metaphor Name; Intent Catalog.
  - D. Patterns For Making Patterns Understandable: Clear Target Audience; Terminology Tailored to Audience; Understood Notations; Code Samples; Code Samples as Bonus.
  - E. Pattern Language Structuring Patterns: Pattern Language Summary; Problem/Solution Summary; Common Problems Highlighted; Running Example; (Distinctive) Headings Convey Structure; Glossary.
  — [hillside.net](https://hillside.net/language-for-pattern-writing)
- A summary of "Mandatory Elements Present" lists Name, Problem, Context, Forces and Solution as "the minimum information required to effectively communicate a pattern". — [Pitt course page](https://people.cs.pitt.edu/~chang/231/8pattern/LANGUAGE/project/s10.html)
- "the exact names of these elements vary from one pattern style to another and the exact order in which they appear in is not as crucial as ensuring that they are all present." — [Carleton slides](https://people.scs.carleton.ca/~deugo/Patterns/Agent/Presentations/AgentPatterns/tsld116.htm)
- If necessary elements are missing, it is much harder to know if the pattern solves the reader's problem in an acceptable way. There is no single correct style. — [Pitt course page](https://people.cs.pitt.edu/~chang/231/8pattern/LANGUAGE/project/s10.html)
- Wikipedia also says that there is no single standard format for design patterns. — [Wikipedia: Software design pattern](https://en.wikipedia.org/wiki/Software_design_pattern)
- Anti-pattern: Azure defines it as "a practice to avoid". It often starts as a reasonable design that works in tests or at low scale and then degrades. — [Azure Cloud design patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/)

### Inferences
- The PLoP view sets a minimum contract (name, context, problem, forces, solution), not a fixed form. Forces are the core of judgment: the solution is a balance of forces, and the writer must make the forces visible.

### Gaps
- The c2 wiki page on pattern forms did not load (JavaScript only). Thus these items are unverified (background knowledge):
  - Portland form: narrative paragraphs, with a problem paragraph, a "Therefore," and a solution paragraph. It comes from the Portland Pattern Repository (Ward Cunningham).
  - Coplien form (also "canonical form"): Name, Problem, Context, Forces, Solution, Resulting Context, Rationale. Some versions add Sketch, Examples, Known Uses and Related Patterns.
  - Optional elements in Meszaros and Doble: Indications (symptoms), Resulting Context, Rationale, Related Patterns, Examples, Code Samples, Aliases, Acknowledgments.
  - "Resulting context" is the state after the pattern is applied, with the forces that it resolves and the new forces that it adds. It is the link to the next patterns.
  - "Rule of three": a solution is a pattern only after three independent known uses. A candidate without them is a "proto-pattern". This is a convention of the Hillside / PLoP community. I found no primary source in this session.
  - Anti-pattern as a format: Koenig (1995) and Brown et al., *AntiPatterns* (1998), with sections for the bad solution and a "refactored solution".
- I did not reach the full PDF of Meszaros and Doble.

## Fowler (PoEAA) and Hohpe and Woolf (Enterprise Integration Patterns)

### Takeaway
PoEAA has 51 patterns in 10 groups. Each pattern has a short intent sentence and a sketch, then "How It Works", "When to Use It", optional "Further Reading", and one or more "Example" sections. EIP has 65 patterns in seven categories that follow the path of a message, and each pattern has a name, an icon, a problem, a solution and a sketch.

### Cited Findings
- PoEAA catalog: 51 patterns in 10 groups. Domain Logic (4), Data Source Architectural (4), Object-Relational Behavioral (3), Object-Relational Structural (10), Object-Relational Metadata Mapping (3), Web Presentation (7), Distribution (2), Offline Concurrency (4), Session State (3), Base (11). — [martinfowler.com/eaaCatalog](https://martinfowler.com/eaaCatalog/)
- The catalog site has "short summaries of these patterns, with deep links to the relevant chapters". The book collects patterns that Fowler and colleagues "have seen in these systems over the years." — [martinfowler.com/eaaCatalog](https://martinfowler.com/eaaCatalog/)
- The table of contents shows "How It Works" and "When to Use It" under almost all patterns. "Further Reading" is on many patterns, but not all. Code examples are "Example: ... (Java)" or "(C#)". Some patterns have many examples and some have none. — [PoEAA table of contents (uni.li PDF)](https://toc.uni.li/FLMF008074.pdf); [O'Reilly listing](https://www.oreilly.com/library/view/patterns-of-enterprise/0321127420/index.html)
- The book has narrative chapters first, and then the pattern reference, which is most of the book. — [culttt review](https://culttt.onrender.com/2015/03/11/patterns-of-enterprise-application-architecture-review)
- EIP: 65 patterns in seven categories: Integration Styles; Channel Patterns; Message Construction Patterns; Routing Patterns; Transformation Patterns; Endpoint Patterns; System Management Patterns. — [enterpriseintegrationpatterns.com](https://www.enterpriseintegrationpatterns.com/patterns/messaging/)
- The name, the icon, the "problem and solution statements, and the sketches" are reusable under CC-BY. The sketch is "the diagram below the solution statement." — [enterpriseintegrationpatterns.com](https://www.enterpriseintegrationpatterns.com/patterns/messaging/)

### Inferences
- PoEAA routes the reader through narrative chapters that compare the options (for example, Transaction Script vs Domain Model vs Table Module), and then sends the reader to the reference section. "When to Use It" holds the selection judgment.
- EIP orders its categories by the flow of a message (style, channel, construction, routing, transformation, endpoint, management). The icons make a visual language: the reader can draw a full solution as a chain of pattern icons.

### Gaps
- Unverified (background knowledge): the EIP pattern form is Name; icon; context; problem (bold, as a question); forces; "Therefore," solution (bold); sketch; results and next steps (resulting context with links to related patterns); sidebars; examples. Each chapter starts with an introduction that maps its patterns. I did not fetch a page that states this form.
- Unverified: in the PoEAA preface, Fowler says that each pattern starts with a name, an intent and a sketch. He describes the "When to Use It" section as the place for the trade-offs.

## Cloud catalogs: Azure Architecture Center, AWS Prescriptive Guidance and Well-Architected, Google Cloud Architecture Framework

### Takeaway
Azure routes the reader by problem and by Well-Architected pillar. Each pattern page has fixed sections: "Context and problem", "Solution", "Problems and considerations", "When to use this pattern", "Workload design" (a pillar table), "Example" and "Related resources". AWS splits patterns into a design pattern guide and a large implementation catalog by technical domain, and the Well-Architected Framework (6 pillars) is the review layer. Google has 6 pillars and 2 cross-pillar perspectives.

### Cited Findings
- Azure pattern page headings, in order (Circuit Breaker, updated 2026-11-09): intent paragraph (no heading); "Context and problem"; "Solution"; "Problems and considerations"; "When to use this pattern" (with "Use this pattern when:" and "This pattern might not be suitable when:"); "Workload design"; "Example" (with sub-flows and "Components"); "Related resources". — [Azure: Circuit Breaker pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- The page heading is now "Problems and considerations", not the older "Issues and considerations". — [Azure: Circuit Breaker pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- "Workload design" has a table with columns "Pillar" and "How this pattern supports pillar goals". Each row links to Well-Architected guidance codes, for example "RE:03 Failure mode analysis", "RE:07 Self-preservation", "PE:07 Code and infrastructure". It ends: "If this pattern introduces trade-offs within a pillar, consider them against the goals of the other pillars." — [Azure: Circuit Breaker pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- Azure catalog index (ms.date 2026-05-03, updated 2026-11-13): 44 patterns in one table with columns "Pattern", "Summary", "Well-Architected Framework pillars". Index headings: "How cloud design patterns enhance the design process", "Select a pattern", "Pattern catalog", "Combine patterns", "Antipatterns", "AI agent orchestration patterns". — [Azure Cloud design patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/)
- Azure selection rule: "Choose a pattern based on the problem you need to solve, not the technology you want to use." and "A pattern is a good fit when its problem statement matches the challenge you face and when the trade-offs it introduces are ones you can accept in your workload." — [Azure Cloud design patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/)
- Azure: "Each cloud design pattern has trade-offs. Focus on why you should choose a specific pattern instead of how to implement it." Each pattern "represents one or more" Well-Architected pillars. — [Azure Cloud design patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/)
- Azure composition: "Patterns are composable. A single pattern addresses one problem, but a workload usually faces several problems at once". Examples: Retry with Circuit Breaker; Queue-Based Load Leveling with Competing Consumers; Gateway Routing, Aggregation and Offloading behind one endpoint; Saga built on Compensating Transaction. — [Azure Cloud design patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/)
- Azure gives the fallacies of distributed computing as the reason for patterns. It adds two of its own: "Component versioning is simple." and "Observability implementation can be delayed." — [Azure Cloud design patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/)
- The Circuit Breaker page states how to combine it with Retry: the retry logic must stop when the breaker shows that a fault is not transient. — [Azure: Circuit Breaker pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- AWS Well-Architected Framework, publication date November 6, 2024. Six pillars: operational excellence, security, reliability, performance efficiency, cost optimization, sustainability. — [AWS WAF welcome](https://docs.aws.amazon.com/wellarchitected/latest/framework/welcome.html); [AWS WAF pillars](https://docs.aws.amazon.com/wellarchitected/latest/framework/the-pillars-of-the-framework.html)
- AWS: the framework "documents a set of foundational questions". The review "is a constructive conversation about architectural decisions, and is not an audit mechanism." The AWS Well-Architected Tool "provides a consistent process for you to review and measure your architecture". — [AWS WAF welcome](https://docs.aws.amazon.com/wellarchitected/latest/framework/welcome.html)
- AWS Prescriptive Guidance patterns: "step-by-step instructions, architecture, tools, and code for implementing specific cloud migration, modernization, and deployment scenarios". Each pattern "includes details such as prerequisites, target reference architectures, tools, step-by-step tasks, best practices, troubleshooting, and code." — [AWS PG patterns](https://docs.aws.amazon.com/prescriptive-guidance/latest/patterns/welcome.html)
- AWS PG routes by technical domain: Cloud foundations; AI & machine learning; Analytics; Compute; Databases & storage; Developer tools; IoT; Migration & modernization; Management; Security, identity & compliance. It also routes by migration strategy: "relocate, rehost, replatform, or rearchitect". — [AWS PG patterns](https://docs.aws.amazon.com/prescriptive-guidance/latest/patterns/welcome.html)
- AWS PG pattern pages use the headings "Prerequisites and limitations", "Tools" and "Epics" (PDF table of contents). — [AWS PG patterns PDF](https://docs.aws.amazon.com/pdfs/prescriptive-guidance/latest/patterns/prescriptive-guidance.pdf)
- AWS has a separate design pattern guide, "Cloud design patterns, architectures, and implementations": Anti-corruption layer; API routing (hostname, path, HTTP header); Circuit breaker; Event sourcing; Hexagonal architecture; Publish-subscribe; Retry with backoff; Saga (choreography, orchestration); Scatter-gather; Strangler fig; Transactional outbox. Each pattern has "issues and considerations", "a high-level architectural implementation", and "the AWS implementation". — [AWS PG cloud design patterns](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/introduction.html)
- Google Cloud Well-Architected Framework: six pillars: Operational excellence; Security, privacy, and compliance; Reliability; Cost optimization; Performance optimization; Sustainability. Two cross-pillar perspectives: AI and ML; Financial services. A pillar "provides principles and recommendations for a specific non-functional focus area." — [Google Cloud Architecture Framework](https://docs.cloud.google.com/architecture/framework)
- Google core principles: Design for change; Document your architecture; Simplify your design and use fully managed services; Decouple your architecture; Use a stateless architecture. Each pillar has an agent skill with core principles, products, assessment questions and a validation checklist. — [Google Cloud Architecture Framework](https://docs.cloud.google.com/architecture/framework)

### Inferences
- Cloud catalogs keep the old core (context and problem, solution, forces as "considerations", applicability) and add two new things: a negative applicability list ("might not be suitable when") and a link from each pattern to quality pillars. Forces become named pillar trade-offs.
- Routing has two levels. The pattern catalog routes by problem (and pillar tag). The Well-Architected framework routes by review question: a review finds a risk, and the risk points to a pattern.
- Fixed versus judgment: the problem statement, the mechanism and the pillar mapping are fixed. Thresholds, time-outs and the choice to combine are judgment ("Problems and considerations").
- When no pattern fits: Azure tells the reader to start from the constraint or risk, not from the technology. The Well-Architected questions and principles still apply when no pattern matches.

### Gaps
- Unverified (background knowledge): AWS Well-Architected lenses (for example Serverless, SaaS, Machine Learning, Generative AI) add questions for a domain. The Tool marks each answer as a high risk issue (HRI) or a medium risk issue (MRI) and makes an improvement plan. Pillar documents have design principles, best practice areas, questions (for example REL 1) and best practices. I did not fetch these pages.
- I did not fetch a full AWS PG pattern page. Thus the complete heading list (for example "Summary", "Prerequisites and limitations", "Architecture", "Tools", "Best practices", "Epics", "Troubleshooting", "Related resources") is unverified.
- The Google page has no "last updated" date. I did not check its "What's new" page.
