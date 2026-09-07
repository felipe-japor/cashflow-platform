// Todos os testes deste projeto são caixa-preta contra os mesmos containers Docker de longa
// duração (Postgres/RabbitMQ reais, não resetados entre execuções - ver README.md) e usam "hoje"
// como data do lançamento, acumulando no mesmo registro de consolidado diário. Rodar classes de
// teste em paralelo (comportamento padrão do xUnit, uma collection por classe) faria dois testes
// lerem o mesmo baseline antes de o outro gravar seu delta, quebrando a asserção por corrida - não
// é flakiness de infraestrutura, é ordem de execução dos próprios testes. Mais simples desabilitar
// paralelismo no assembly inteiro do que inventar isolamento de data por teste.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
