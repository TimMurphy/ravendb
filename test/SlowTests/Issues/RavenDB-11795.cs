﻿using System;
using System.Globalization;
using System.Linq;
using FastTests;
using Tests.Infrastructure;
using Raven.Client.Documents.Indexes;
using Raven.Server.Utils;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_11795 : RavenTestBase
    {
        public RavenDB_11795(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [RavenData(JavascriptEngineMode = RavenJavascriptEngineMode.Jint)]
        public void SearchBooking_ProjectionWithDateTimeToStringAndFormat_ReturnsResult(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                // Arrange  
                store.ExecuteIndex(new BookingIndex());

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForIndexesAfterSaveChanges(TimeSpan.FromSeconds(2));

                    session.Store(new Booking { FirstName = "Alex", LastName = "Me", Start = DateTime.Parse("2018-01-01T11:11:11") });
                    session.Store(new Booking { FirstName = "You", LastName = "Me", Start = DateTime.Parse("2017-11-11T10:10:10") });
                    session.SaveChanges();
                }

                Indexes.WaitForIndexing(store);

                // Act
                using (var session = store.OpenSession())
                {
                    var query = session.Query<BookingIndex.Result, BookingIndex>()
                        .Where(x => x.FullName == "Alex Me")
                        .Select(x => new
                        {
                            FullName = x.FullName,
                            StartDate = x.Start.ToString("dd.MM.yyyy")
                        });

                    Assert.Equal("from index 'BookingIndex' as x where x.FullName = $p0 " +
                                 "select { FullName : x?.FullName, StartDate : toStringWithFormat(x?.Start, \"dd.MM.yyyy\") }"
                            , query.ToString());

                    var result = query.Single();

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal("01.01.2018", result.StartDate);

                }
            }
        }

        [Theory]
        [RavenData(JavascriptEngineMode = RavenJavascriptEngineMode.Jint)]
        public void DateToStringWithInvariantCulture(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var start = DateTime.Parse("2018-01-01T11:11:11");

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForIndexesAfterSaveChanges(TimeSpan.FromSeconds(2));

                    session.Store(new Booking { FirstName = "Alex", LastName = "Me", Start = start });
                    session.Store(new Booking { FirstName = "You", LastName = "Me", Start = DateTime.Parse("2017-11-11T10:10:10") });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Booking>()
                        .Where(x => x.FirstName == "Alex")
                        .Select(x => new
                        {
                            StartDate = x.Start.ToString(CultureInfo.InvariantCulture)
                        });

                    Assert.Equal("from 'Bookings' as x where x.FirstName = $p0 " +
                                 "select { StartDate : toStringWithFormat(x?.Start) }"
                        , query.ToString());

                    var result = query.Single();

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal(start.ToString(CultureInfo.InvariantCulture), result.StartDate);

                }
            }
        }
        
        [Theory]
        [RavenData(JavascriptEngineMode = RavenJavascriptEngineMode.Jint)]
        public void DateToStringWithCurrentCulture(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var start = DateTime.Parse("2018-01-01T11:11:11");

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForIndexesAfterSaveChanges(TimeSpan.FromSeconds(2));

                    session.Store(new Booking { FirstName = "Alex", LastName = "Me", Start = start });
                    session.Store(new Booking { FirstName = "You", LastName = "Me", Start = DateTime.Parse("2017-11-11T10:10:10") });
                    session.SaveChanges();
                }

                CultureHelper.Cultures.TryGetValue(CultureInfo.CurrentCulture.Name, out var culture);

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Booking>()
                        .Where(x => x.FirstName == "Alex")
                        .Select(x => new
                        {
                            StartDate = x.Start.ToString(CultureInfo.CurrentCulture)
                        });

                    Assert.Equal("from 'Bookings' as x where x.FirstName = $p0 " +
                                 $"select {{ StartDate : toStringWithFormat(x?.Start, '{culture.Name}') }}"
                        , query.ToString());

                    var result = query.Single();

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal(start.ToString(culture), result.StartDate);

                }
            }
        }

        [Theory]
        [RavenData(JavascriptEngineMode = RavenJavascriptEngineMode.Jint)]
        public void DateToStringWithFormatAndCurrentCulture(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var start = DateTime.Parse("2018-01-01T11:11:11");

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForIndexesAfterSaveChanges(TimeSpan.FromSeconds(2));

                    session.Store(new Booking { FirstName = "Alex", LastName = "Me", Start = start });
                    session.Store(new Booking { FirstName = "You", LastName = "Me", Start = DateTime.Parse("2017-11-11T10:10:10") });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Booking>()
                        .Where(x => x.FirstName == "Alex")
                        .Select(x => new
                        {
                            StartDate = x.Start.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)
                        });

                    Assert.Equal("from 'Bookings' as x where x.FirstName = $p0 " +
                                 $"select {{ StartDate : toStringWithFormat(x?.Start, \"dd.MM.yyyy\", '{CultureInfo.CurrentCulture.Name}') }}"
                        , query.ToString());

                    var result = query.Single();

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal(start.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture), result.StartDate);

                }
            }
        }


        [Theory]
        [RavenData(JavascriptEngineMode = RavenJavascriptEngineMode.Jint)]
        public void DateToStringWithFormatAndInvariantCulture(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var start = DateTime.Parse("2018-01-01T11:11:11");

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForIndexesAfterSaveChanges(TimeSpan.FromSeconds(2));

                    session.Store(new Booking { FirstName = "Alex", LastName = "Me", Start = start });
                    session.Store(new Booking { FirstName = "You", LastName = "Me", Start = DateTime.Parse("2017-11-11T10:10:10") });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Booking>()
                        .Where(x => x.FirstName == "Alex")
                        .Select(x => new
                        {
                            StartDate = x.Start.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                        });

                    Assert.Equal("from 'Bookings' as x where x.FirstName = $p0 " +
                                 "select { StartDate : toStringWithFormat(x?.Start, \"dd.MM.yyyy\") }"
                        , query.ToString());

                    var result = query.Single();

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal(start.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), result.StartDate);

                }
            }
        }

        [Theory]
        [RavenData(JavascriptEngineMode = RavenJavascriptEngineMode.Jint)]
        public void NumberToStringWithFormat(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var num = 12345000;

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForIndexesAfterSaveChanges(TimeSpan.FromSeconds(2));

                    session.Store(new Booking { FirstName = "Alex", LastName = "Me", Number = num });
                    session.Store(new Booking { FirstName = "You", LastName = "Me", Number = 20 });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Booking>()
                        .Where(x => x.FirstName == "Alex")
                        .Select(x => new
                        {
                            Number = x.Number.ToString("000")
                        });

                    Assert.Equal("from 'Bookings' as x where x.FirstName = $p0 " +
                                 "select { Number : toStringWithFormat(x?.Number, \"000\") }"
                        , query.ToString());

                    var result = query.Single();

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal(num.ToString("000"), result.Number);
                }
            }
        }

        [Theory]
        [RavenData(JavascriptEngineMode = RavenJavascriptEngineMode.Jint)]
        public void NumberToStringWithInvariantCulture(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var num = 12345000;

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForIndexesAfterSaveChanges(TimeSpan.FromSeconds(2));

                    session.Store(new Booking { FirstName = "Alex", LastName = "Me", Number = num });
                    session.Store(new Booking { FirstName = "You", LastName = "Me", Number = 20 });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Booking>()
                        .Where(x => x.FirstName == "Alex")
                        .Select(x => new
                        {
                            Number = x.Number.ToString(CultureInfo.InvariantCulture)
                        });

                    Assert.Equal("from 'Bookings' as x where x.FirstName = $p0 " +
                                 "select { Number : toStringWithFormat(x?.Number) }"
                        , query.ToString());

                    var result = query.Single();

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal(num.ToString(CultureInfo.InvariantCulture), result.Number);

                }
            }
        }

        [Theory]
        [RavenData(JavascriptEngineMode = RavenJavascriptEngineMode.Jint)]
        public void NumberToStringWithCurrentCulture(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var num = 12345000;

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForIndexesAfterSaveChanges(TimeSpan.FromSeconds(2));

                    session.Store(new Booking { FirstName = "Alex", LastName = "Me", Number = num });
                    session.Store(new Booking { FirstName = "You", LastName = "Me", Number = 20 });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Booking>()
                        .Where(x => x.FirstName == "Alex")
                        .Select(x => new
                        {
                            Number = x.Number.ToString(CultureInfo.CurrentCulture)
                        });

                    Assert.Equal("from 'Bookings' as x where x.FirstName = $p0 " +
                                 $"select {{ Number : toStringWithFormat(x?.Number, '{CultureInfo.CurrentCulture.Name}') }}"
                        , query.ToString());

                    var result = query.Single();

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal(num.ToString(CultureInfo.InvariantCulture), result.Number);

                }
            }
        }

        [Theory]
        [RavenData(JavascriptEngineMode = RavenJavascriptEngineMode.Jint)]
        public void NumberToStringWithFormatAndCulture(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var num = 12345000;

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForIndexesAfterSaveChanges(TimeSpan.FromSeconds(2));

                    session.Store(new Booking { FirstName = "Alex", LastName = "Me", Number = num });
                    session.Store(new Booking { FirstName = "You", LastName = "Me", Number = 20 });
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var query = session.Query<Booking>()
                        .Where(x => x.FirstName == "Alex")
                        .Select(x => new
                        {
                            Number = x.Number.ToString("000", CultureInfo.CurrentCulture)
                        });

                    Assert.Equal("from 'Bookings' as x where x.FirstName = $p0 " +
                                 $"select {{ Number : toStringWithFormat(x?.Number, \"000\", '{CultureInfo.CurrentCulture.Name}') }}"
                        , query.ToString());

                    var result = query.Single();

                    // Assert
                    Assert.NotNull(result);
                    Assert.Equal(num.ToString("000", CultureInfo.CurrentCulture), result.Number);

                }
            }
        }

        private class Booking
        {
            public string Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public DateTime Start { get; set; }
            public int Number { get; set; }
        }

        private class BookingIndex : AbstractIndexCreationTask<Booking>
        {
            public class Result
            {
                public string Id { get; set; }
                public string FullName { get; set; }
                public DateTime Start { get; set; }
            }

            public BookingIndex()
            {
                Map = bookings => from booking in bookings
                                  select new Result
                                  {
                                      Id = booking.Id,
                                      FullName = booking.FirstName + " " + booking.LastName,
                                      Start = booking.Start,
                                  };

                Store("FullName", FieldStorage.Yes);
            }
        }
    }
}
