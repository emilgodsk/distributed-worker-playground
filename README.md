

## Purpose

To explore the ways of handling long-running distributed workers.

### Background

While exploring the usage of queues such as RabbitMQ for handling a FIFO like queue to enable workers to pick up tasks to be fulfilled, I ran across the following: [Delivery Acknowledge Timeout](https://www.rabbitmq.com/docs/consumers#acknowledgement-timeout). This describes that RabbitMQ has a hard limit defined by the timeout value, after which it will neg-ack the message, if the consumer has not acknowledged it. This brings some concerns to the use-case this technology was considered for: handling long-running tasks.

### Usage of other technologies

At the moment, the usage of Azure ServiceBus is in active use. This is working without issue, but is due to a major difference: Azure ServiceBus has the ability to lock & extend the lock when consuming message ([PeekLock](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-transfers-locks-settlement#peeklock)) which is ideal for making sure long-running tasks as not neg-ack, if the consumer is still active.

So why not keep using Azure ServiceBus? Due to decisions, it has been determined that the offboarding of Azure is necessary. Therefore, the need for an alternative is needed.

### Options

1. RabbitMQ
2. Postgresql

### Excluding the usage of RabbitMQ

As mentioned above in the Background section, due to the hard-limit of the timeout based on the configured timeout value of RabbitMQ, and the inability to extend a lock of a message being consumed, I do not want to consider the usage of RabbitMQ for handling the consumption of message for long-running tasks. Down in the observations section I will go into more detail about the exploration of RabbitMQ, and verifying that it is indeed not suitable for this use-case.

### Alternative & Architecture

This leaves me with limited options. In this repository I plan to explore the usage of Postgresql for handling a queue-like structure, with the ability to 'lease' messages/jobs.

This should be made in such a way that any consumers handling messages/jobs pulled from the database, should be able to constantly ping back a heartbeat letting the database know that it is still leasing the message/job. This should allow for any consumer to run their job for as long as needed, as well as enable the job to timeout in case a worker somehow crashes out, without manually marking the message/job as 'neg-ack'.

The system should be able to run using a single scheduler that retrieves the jobs and distributes them to a poll of X workers. This should be explored by using .NET channels for handling the concurrency of having multiple workers working on distributed jobs, while not having to all be responsible for having the logic & overhead of finding the jobs & thereby limit the amount of actual connections to the service that is responsible for getting the jobs.

### Usage of an existing library

I explored the usage of other external libraries for handling this use-case, but found very limited libraries that handled the specific requirements I want to be solved. This included the ability to have this work across a distributed system, where the workers & scheduler do not necessarily own the data-store, and could therefore not rely on technologies for locking such as SQL transaction locking for keeping a job occupied.

#### Quartz.NET

A job framework that supports easy setup of having consumers of jobs as well as handling triggers of jobs. However, it does not support/handle the concept of having an easy way to support the distributed locking of objects, as it uses the database for handling the locking of objects.

#### Hangfire

Previously discounted for exploration due to not trusting the way jobs are serialized/deserialized in the datastore. It seemed a bit too magical, and is therefore excluded.

#### Others?

I have not found other good alternatives for .NET.

## Observations

Below are some of the observations made while exploring the ideas of having a distributed worker system for handling long-running tasks.

### Using RabbitMQ

The usage of RabbitMQ is indeed limited by the inability to extend the lock of a message being consumed. This makes it unsuitable for long-running tasks. While it is possible to set a very high timeout value, this is not ideal as it could lead to messages being stuck in-flight for a long time if a worker crashes. This is also not a scalable solution, as the timeout value would have to be adjusted based on the expected duration of the tasks being processed, which could vary greatly.

I explored how the default RabbitMQ client handled the consumption of messages, and found that it did not have any built-in mechanism for recovering upon a timeout. Therefore, I would have to implement my own mechanism for handling this, which would add complexity to the system. (See RabbitMQSender and RabbitMQReceiver in this repository for an example of how this could be implemented.)

I further more explored how MassTransit handled this, and found that it had a built-in mechanism for handling message retries upon failure. However, this still did not solve the problem of having long-running tasks, as the timeout would still be a hard limit. MassTransit had a mechanism for recovering from consumer timeouts, however, while a message was being consumed, and the connection was lost, it would not consumer messages while the one message was being processed. This was not easily seen until the connection tried to mark the message it was working on as acknowledged, and failed. This also lead to the message being requeued, even though it completed successfully. (See MassTransitSender and MassTransitReceiver in this repository for an example of how this could be implemented.)
